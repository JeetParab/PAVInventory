using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Core.Services;

public class IpAddressService(AppDbContext db, IWriteLock writeLock)

{
    public static readonly string[] DeviceTypes =
    [
        "Desktop", "Laptop", "Server", "Printer", "Access Point", "Router",
        "Switch", "VoIP Phone", "Firewall", "Other", "Unknown"
    ];

    public async Task<IpOverviewDto> OverviewAsync()
    {
        var ranges = await db.IpRanges.AsNoTracking().OrderBy(r => r.FloorNumber).ToListAsync();
        var rows = await db.IpRecords.AsNoTracking()
            .Select(x => new { x.RangeId, x.Status, x.HostOctet, x.Address })
            .ToListAsync();

        var grouped = rows.GroupBy(x => x.RangeId).ToDictionary(g => g.Key, g => g.ToList());
        var floors = new List<IpFloorSummaryDto>();
        var total = 0;
        var used = 0;
        var free = 0;
        var reserved = 0;

        foreach (var range in ranges)
        {
            grouped.TryGetValue(range.Id, out var list);
            list ??= [];
            var u = list.Count(x => x.Status == IpStatus.Used);
            var f = list.Count(x => x.Status == IpStatus.Free);
            var r = list.Count(x => x.Status == IpStatus.Reserved);
            var next = list.Where(x => x.Status == IpStatus.Free).OrderBy(x => x.HostOctet).FirstOrDefault();
            var t = list.Count;
            total += t;
            used += u;
            free += f;
            reserved += r;
            var util = t == 0 ? 0 : (u * 100.0 / t);
            floors.Add(new IpFloorSummaryDto
            {
                RangeId = range.Id,
                FloorNumber = range.FloorNumber,
                Name = range.Name,
                Cidr = range.Cidr,
                Total = t,
                Used = u,
                Free = f,
                Reserved = r,
                NextFreeIp = next?.Address,
                UtilLabel = $"{util:0.#}%"
            });
        }

        return new IpOverviewDto
        {
            Total = total,
            Used = used,
            Free = free,
            Reserved = reserved,
            Floors = floors
        };
    }

    public async Task<List<IpAddressDto>> ListAsync(int? rangeId, string? statusFilter, string? search)
    {
        var q = db.IpRecords.AsNoTracking().Include(x => x.Range).AsQueryable();
        if (rangeId is > 0)
            q = q.Where(x => x.RangeId == rangeId);

        var filter = (statusFilter ?? "allocated").Trim().ToLowerInvariant();
        q = filter switch
        {
            "used" => q.Where(x => x.Status == IpStatus.Used),
            "reserved" => q.Where(x => x.Status == IpStatus.Reserved),
            "free" => q.Where(x => x.Status == IpStatus.Free),
            "quarantine" => q.Where(x => x.Status == IpStatus.Quarantine),
            "deprecated" => q.Where(x => x.Status == IpStatus.Deprecated),
            "temporary" => q.Where(x => x.IsTemporary),
            "all" => q,
            _ => q.Where(x => x.Status == IpStatus.Used || x.Status == IpStatus.Reserved || x.Status == IpStatus.Quarantine)
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x =>
                x.Address.ToLower().Contains(s) ||
                (x.AssignedDevice != null && x.AssignedDevice.ToLower().Contains(s)) ||
                (x.AssignedUser != null && x.AssignedUser.ToLower().Contains(s)) ||
                (x.Department != null && x.Department.ToLower().Contains(s)) ||
                (x.MacAddress != null && x.MacAddress.ToLower().Contains(s)) ||
                (x.Notes != null && x.Notes.ToLower().Contains(s)) ||
                (x.DeviceType != null && x.DeviceType.ToLower().Contains(s)));
        }

        var list = await q
            .OrderBy(x => x.Range.FloorNumber)
            .ThenBy(x => x.HostOctet)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<IpCheckResultDto> CheckAsync(string? address)
    {
        if (!TryNormalize(address, out var ip, out _, out _))
        {
            return new IpCheckResultDto
            {
                InPool = false,
                Address = address?.Trim() ?? "",
                Message = "Enter a valid IPv4 address, e.g. 172.16.103.178."
            };
        }

        var rec = await db.IpRecords.AsNoTracking()
            .Include(x => x.Range)
            .FirstOrDefaultAsync(x => x.Address == ip);

        if (rec is null)
        {
            return new IpCheckResultDto
            {
                InPool = false,
                Address = ip,
                Message = "This IP is not in the Floor 1–7 static pool (172.16.101–107)."
            };
        }

        var dto = ToDto(rec);
        var msg = rec.Status switch
        {
            IpStatus.Free => "Free — you can assign this IP.",
            IpStatus.Used => "Already allocated.",
            IpStatus.Reserved => "Reserved — not for end-user assignment.",
            IpStatus.Quarantine => "Quarantine — do not assign.",
            IpStatus.Deprecated => "Deprecated — do not assign.",
            _ => rec.Status.Display()
        };

        return new IpCheckResultDto
        {
            InPool = true,
            Address = ip,
            Message = msg,
            Record = dto
        };
    }

    public Task<IpAddressDto> AssignAsync(AssignIpRequest req, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            IpRecord? rec = null;
            if (req.Id is > 0)
                rec = await db.IpRecords.Include(x => x.Range).FirstOrDefaultAsync(x => x.Id == req.Id);
            else if (!string.IsNullOrWhiteSpace(req.Address) && TryNormalize(req.Address, out var ip, out _, out _))
                rec = await db.IpRecords.Include(x => x.Range).FirstOrDefaultAsync(x => x.Address == ip);
            else if (req.RangeId is > 0)
            {
                rec = await db.IpRecords.Include(x => x.Range)
                    .Where(x => x.RangeId == req.RangeId && x.Status == IpStatus.Free)
                    .OrderBy(x => x.HostOctet)
                    .FirstOrDefaultAsync();
                if (rec is null)
                    throw new AppException(400, "validation", "No free IP on that floor.");
            }

            if (rec is null)
                throw new AppException(404, "not_found", "IP address was not found in the pool.");

            if (rec.Status is not IpStatus.Free)
                throw new AppException(409, "conflict",
                    $"{rec.Address} is {rec.Status.Display().ToLowerInvariant()}." +
                    (string.IsNullOrWhiteSpace(rec.AssignedUser) ? "" : $" Assigned to {rec.AssignedUser}."));

            rec.Status = IpStatus.Used;
            rec.IsTemporary = req.IsTemporary;
            rec.AssignedDevice = Clean(req.AssignedDevice);
            rec.AssignedUser = Clean(req.AssignedUser);
            rec.Department = Clean(req.Department);
            rec.MacAddress = Clean(req.MacAddress);
            rec.DeviceType = Clean(req.DeviceType);
            rec.Notes = Clean(req.Notes);
            rec.DateAssigned = DateTime.UtcNow;
            rec.LastUpdated = DateTime.UtcNow;
            rec.AllocatedBy = actor.DisplayName;
            await SqliteGuard.SaveChangesAsync(db);
            var dto = ToDto(rec);
            var addAsset = req.AddToInventory && !req.IsTemporary;
            dto.InventorySync = await IpAssetBridge.AfterIpAssignedAsync(db, rec, addAsset, actor);
            if (addAsset && dto.InventorySync is not null)
                dto.InventorySync += " Serial and make/model still needed — see Pending.";
            if (req.IsTemporary)
                dto.InventorySync = $"{rec.Address} is temporary — kept in IP Inventory only, not added as office kit.";
            return dto;
        });

    public Task<IpAddressDto> ReserveAsync(int id, string? notes, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var rec = await db.IpRecords.Include(x => x.Range).FirstOrDefaultAsync(x => x.Id == id)
                      ?? throw new AppException(404, "not_found", "IP address was not found.");
            if (rec.Status == IpStatus.Used)
                throw new AppException(409, "conflict", $"{rec.Address} is in use. Release it first.");
            rec.Status = IpStatus.Reserved;
            if (!string.IsNullOrWhiteSpace(notes))
                rec.Notes = Clean(notes);
            rec.LastUpdated = DateTime.UtcNow;
            rec.AllocatedBy = actor.DisplayName;
            await SqliteGuard.SaveChangesAsync(db);
            return ToDto(rec);
        });

    public Task<IpAddressDto> ReleaseAsync(int id, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var rec = await db.IpRecords.Include(x => x.Range).FirstOrDefaultAsync(x => x.Id == id)
                      ?? throw new AppException(404, "not_found", "IP address was not found.");
            rec.Status = IpStatus.Free;
            rec.IsTemporary = false;
            rec.AssignedDevice = null;
            rec.AssignedUser = null;
            rec.Department = null;
            rec.MacAddress = null;
            rec.DeviceType = null;
            rec.DateAssigned = null;
            rec.LastUpdated = DateTime.UtcNow;
            rec.AllocatedBy = actor.DisplayName;
            var address = rec.Address;
            await SqliteGuard.SaveChangesAsync(db);
            await IpAssetBridge.AfterIpReleasedAsync(db, address, actor);
            rec = await db.IpRecords.Include(x => x.Range).FirstAsync(x => x.Id == rec.Id);
            return ToDto(rec);
        });

    public Task<IpImportResult> ImportWorkbookAsync(Stream stream, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            using var wb = new XLWorkbook(stream);
            var floorsSheet = FindSheet(wb, "Floors_Config") ?? FindSheet(wb, "Floors");
            var ipSheet = FindSheet(wb, "IP_Inventory") ?? FindSheet(wb, "Inventory");
            if (ipSheet is null)
                throw new AppException(400, "validation", "Workbook needs an IP_Inventory sheet.");

            var result = new IpImportResult();
            var rangeByFloor = new Dictionary<int, IpRange>();
            var rangeByOctet = new Dictionary<int, IpRange>();

            var existingRanges = await db.IpRanges.ToListAsync();
            foreach (var r in existingRanges)
            {
                rangeByFloor[r.FloorNumber] = r;
                rangeByOctet[r.ThirdOctet] = r;
            }

            if (floorsSheet is not null)
            {
                var headers = Headers(floorsSheet);
                var last = floorsSheet.LastRowUsed()?.RowNumber() ?? 1;
                for (var row = 2; row <= last; row++)
                {
                    var floor = IntVal(floorsSheet.Cell(row, Col(headers, "Floor", "FloorNumber")));
                    var name = Str(floorsSheet.Cell(row, Col(headers, "Floor_Name", "FloorName", "Name")));
                    var third = IntVal(floorsSheet.Cell(row, Col(headers, "Third_Octet", "ThirdOctet")));
                    var cidr = Str(floorsSheet.Cell(row, Col(headers, "Subnet", "Cidr")));
                    var gw = Str(floorsSheet.Cell(row, Col(headers, "Gateway_IP", "Gateway")));
                    var notes = Str(floorsSheet.Cell(row, Col(headers, "Notes")));
                    if (floor is null && string.IsNullOrWhiteSpace(cidr))
                        continue;
                    if (floor is null || third is null || string.IsNullOrWhiteSpace(cidr))
                    {
                        result.Errors.Add($"Floors_Config row {row}: floor, third octet and subnet are required.");
                        continue;
                    }

                    if (!rangeByFloor.TryGetValue(floor.Value, out var range))
                    {
                        range = new IpRange();
                        db.IpRanges.Add(range);
                        result.Ranges++;
                    }

                    range.FloorNumber = floor.Value;
                    range.Name = string.IsNullOrWhiteSpace(name) ? $"Floor {floor}" : name;
                    range.ThirdOctet = third.Value;
                    range.Cidr = cidr!;
                    range.GatewayIp = gw;
                    range.Notes = notes;
                    rangeByFloor[range.FloorNumber] = range;
                    rangeByOctet[range.ThirdOctet] = range;
                }

                await SqliteGuard.SaveChangesAsync(db);
            }

            var existingIps = await db.IpRecords.ToListAsync();
            var byAddress = existingIps.ToDictionary(x => x.Address, StringComparer.OrdinalIgnoreCase);
            var ipHeaders = Headers(ipSheet);
            var lastIp = ipSheet.LastRowUsed()?.RowNumber() ?? 1;
            var now = DateTime.UtcNow;

            for (var row = 2; row <= lastIp; row++)
            {
                var rawIp = Str(ipSheet.Cell(row, Col(ipHeaders, "IP_Address", "IP", "Address")));
                if (!TryNormalize(rawIp, out var ip, out var octet3, out var host))
                {
                    if (!string.IsNullOrWhiteSpace(rawIp))
                        result.Errors.Add($"IP_Inventory row {row}: invalid IP '{rawIp}'.");
                    continue;
                }

                var floor = IntVal(ipSheet.Cell(row, Col(ipHeaders, "Floor")));
                var floorName = Str(ipSheet.Cell(row, Col(ipHeaders, "Floor Name", "Floor_Name", "FloorName")));
                var subnet = Str(ipSheet.Cell(row, Col(ipHeaders, "Subnet", "Cidr")));
                var statusRaw = Str(ipSheet.Cell(row, Col(ipHeaders, "Status")));
                if (!IpStatusNames.TryParse(statusRaw, out var status))
                    status = IpStatus.Free;

                if (!rangeByOctet.TryGetValue(octet3 ?? -1, out var range) &&
                    floor is { } fn && rangeByFloor.TryGetValue(fn, out range))
                {
                    // found by floor
                }

                if (range is null)
                {
                    var third = octet3 ?? 0;
                    var floorNo = floor ?? GuessFloor(third);
                    range = new IpRange
                    {
                        FloorNumber = floorNo,
                        Name = string.IsNullOrWhiteSpace(floorName) ? $"Floor {floorNo}" : floorName,
                        ThirdOctet = third,
                        Cidr = subnet ?? $"172.16.{third}.0/24",
                        GatewayIp = $"172.16.{third}.1"
                    };
                    db.IpRanges.Add(range);
                    await SqliteGuard.SaveChangesAsync(db);
                    rangeByFloor[range.FloorNumber] = range;
                    rangeByOctet[range.ThirdOctet] = range;
                    result.Ranges++;
                }

                if (!byAddress.TryGetValue(ip, out var rec))
                {
                    rec = new IpRecord { Address = ip };
                    db.IpRecords.Add(rec);
                    byAddress[ip] = rec;
                    result.Addresses++;
                }
                else
                {
                    result.Updated++;
                }

                rec.RangeId = range.Id;
                rec.HostOctet = host;
                rec.Status = status;
                rec.AssignedDevice = Str(ipSheet.Cell(row, Col(ipHeaders, "Assigned_Device", "AssignedDevice", "Device")));
                rec.AssignedUser = Str(ipSheet.Cell(row, Col(ipHeaders, "Assigned_User", "AssignedUser", "User")));
                rec.Department = Str(ipSheet.Cell(row, Col(ipHeaders, "Department")));
                rec.MacAddress = Str(ipSheet.Cell(row, Col(ipHeaders, "MAC_Address", "MAC", "MacAddress")));
                rec.DeviceType = Str(ipSheet.Cell(row, Col(ipHeaders, "Device_Type", "DeviceType")));
                rec.Notes = Str(ipSheet.Cell(row, Col(ipHeaders, "Notes")));
                rec.DateAssigned = DateVal(ipSheet.Cell(row, Col(ipHeaders, "Date_Assigned", "DateAssigned")));
                rec.LastUpdated = DateVal(ipSheet.Cell(row, Col(ipHeaders, "Last_Updated", "LastUpdated"))) ?? now;
                rec.AllocatedBy = actor.DisplayName;
            }

            await SqliteGuard.SaveChangesAsync(db);
            result.Summary =
                $"Imported {result.Ranges} floor(s), {result.Addresses} new IP(s), updated {result.Updated} existing.";
            return result;
        });

    private static int GuessFloor(int thirdOctet) =>
        thirdOctet is >= 101 and <= 107 ? thirdOctet - 100 : thirdOctet;

    private static IpAddressDto ToDto(IpRecord x) => new()
    {
        Id = x.Id,
        RangeId = x.RangeId,
        FloorNumber = x.Range?.FloorNumber ?? 0,
        FloorName = x.Range?.Name ?? "",
        Address = x.Address,
        HostOctet = x.HostOctet,
        Subnet = x.Range?.Cidr ?? "",
        StatusValue = x.Status,
        Status = x.IsTemporary ? "Temporary" : x.Status.Display(),
        AssignedDevice = x.AssignedDevice,
        AssignedUser = x.AssignedUser,
        Department = x.Department,
        DateAssigned = x.DateAssigned,
        MacAddress = x.MacAddress,
        DeviceType = x.DeviceType,
        Notes = x.Notes,
        LastUpdated = x.LastUpdated,
        AllocatedBy = x.AllocatedBy,
        IsTemporary = x.IsTemporary
    };

    public static bool TryNormalize(string? input, out string address, out int? octet3, out int host)
    {
        address = "";
        octet3 = null;
        host = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;
        if (!IPAddress.TryParse(input.Trim(), out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = parsed.GetAddressBytes();
        address = $"{b[0]}.{b[1]}.{b[2]}.{b[3]}";
        octet3 = b[2];
        host = b[3];
        return host is >= 1 and <= 254;
    }

    private static string? Clean(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private static IXLWorksheet? FindSheet(XLWorkbook wb, string name) =>
        wb.Worksheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, int> Headers(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var last = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        for (var c = 1; c <= last; c++)
        {
            var raw = ws.Cell(1, c).GetString().Trim();
            if (raw.Length == 0) continue;
            map[raw] = c;
            map[raw.Replace(" ", "").Replace("_", "")] = c;
        }
        return map;
    }

    private static int Col(Dictionary<string, int> headers, params string[] names)
    {
        foreach (var n in names)
        {
            if (headers.TryGetValue(n, out var c))
                return c;
            var compact = n.Replace(" ", "").Replace("_", "");
            if (headers.TryGetValue(compact, out c))
                return c;
        }
        return 0;
    }

    private static string? Str(IXLCell cell)
    {
        if (cell is null || cell.IsEmpty()) return null;
        var v = cell.GetString().Trim();
        return v.Length == 0 ? null : v;
    }

    private static int? IntVal(IXLCell cell)
    {
        if (cell is null || cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.Number)
            return (int)cell.GetDouble();
        return int.TryParse(cell.GetString().Trim(), out var n) ? n : null;
    }

    private static DateTime? DateVal(IXLCell cell)
    {
        if (cell is null || cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();
        var s = cell.GetString().Trim();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return DateTime.TryParse(s, out d) ? d : null;
    }
}
