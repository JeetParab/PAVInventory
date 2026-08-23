using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;
using PAV.Core.Data;

namespace PAV.Core.Services;

public class ImportExportService(AppDbContext db, SqliteWriteLock writeLock)
{
    private static readonly string[] ExportHeaders =
    [
        "Sr No", "Location", "Asset_Category", "Make_Model", "Serial_Number", "Hostname", "IP_Address",
        "User Name", "Designation", "Status", "Domain", "MAC_Address", "Processor", "RAM", "Storage", "OS",
        "DC_Installed", "AV_Installed", "MS_Office_Version", "MFA_Enabled", "Ivanti_Installed",
        "Admin_Rights", "USB_Access", "Chrome_Updated", "Stock_Availability", "Stock_Working",
        "PM_Completed", "Alternate_User", "Last Connected", "Collect By"
    ];

    public byte[] Export(IReadOnlyList<AssetListDto> assets)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Inventory");
        for (var i = 0; i < ExportHeaders.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = ExportHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B2430");
            cell.Style.Font.FontColor = XLColor.White;
        }

        for (var r = 0; r < assets.Count; r++)
        {
            var a = assets[r];
            var row = r + 2;
            ws.Cell(row, 1).Value = a.SrNo ?? r + 1;
            ws.Cell(row, 2).Value = a.Location ?? "";
            ws.Cell(row, 3).Value = a.Category;
            ws.Cell(row, 4).Value = a.MakeModel;
            ws.Cell(row, 5).Value = a.SerialNumber ?? "";
            ws.Cell(row, 6).Value = a.Hostname ?? "";
            ws.Cell(row, 7).Value = a.IpAddress ?? "";
            ws.Cell(row, 8).Value = a.AssignedUser ?? "";
            ws.Cell(row, 9).Value = a.Designation ?? "";
            ws.Cell(row, 10).Value = a.Status;
            ws.Cell(row, 11).Value = a.Domain ?? "";
            ws.Cell(row, 12).Value = a.MacAddress ?? "";
            ws.Cell(row, 13).Value = a.Processor ?? "";
            ws.Cell(row, 14).Value = a.Ram ?? "";
            ws.Cell(row, 15).Value = a.Storage ?? "";
            ws.Cell(row, 16).Value = a.OperatingSystem ?? "";
            ws.Cell(row, 17).Value = a.DcInstalled ?? "";
            ws.Cell(row, 18).Value = a.AvInstalled ?? "";
            ws.Cell(row, 19).Value = a.MsOfficeVersion ?? "";
            ws.Cell(row, 20).Value = a.MfaEnabled ?? "";
            ws.Cell(row, 21).Value = a.IvantiInstalled ?? "";
            ws.Cell(row, 22).Value = a.AdminRights ?? "";
            ws.Cell(row, 23).Value = a.UsbAccess ?? "";
            ws.Cell(row, 24).Value = a.ChromeUpdated ?? "";
            ws.Cell(row, 25).Value = a.StockAvailability ?? "";
            ws.Cell(row, 26).Value = a.StockWorking ?? "";
            ws.Cell(row, 27).Value = a.PmCompleted ?? "";
            ws.Cell(row, 28).Value = a.AlternateUser ?? "";
            ws.Cell(row, 29).Value = a.LastConnected?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 30).Value = a.CollectBy ?? "";
        }

        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(1, 36);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] Template()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Assets");
        for (var i = 0; i < ExportHeaders.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = ExportHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2F6FED");
            cell.Style.Font.FontColor = XLColor.White;
        }
        ws.Cell(2, 1).Value = 1;
        ws.Cell(2, 2).Value = "Mumbai Office";
        ws.Cell(2, 3).Value = "Laptop";
        ws.Cell(2, 4).Value = "HP ProBook 440 G8 Notebook PC";
        ws.Cell(2, 5).Value = "1N11470998";
        ws.Cell(2, 6).Value = "EXAMPLE-ELT";
        ws.Cell(2, 7).Value = "172.16.101.1";
        ws.Cell(2, 8).Value = "Example User";
        ws.Cell(2, 9).Value = "Engineer";
        ws.Cell(2, 10).Value = "In Use";
        ws.Cell(2, 11).Value = "SIDBIFARM";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 28);

        var help = wb.Worksheets.Add("Help");
        help.Cell(1, 1).Value = "PAV Inventory import";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(2, 1).Value = "CSV or Excel. The SIDBI master sheet (Sr No, Location, Asset_Category, Make_Model, …) imports as-is.";
        help.Cell(3, 1).Value = "Required: a serial number, hostname, or Sr No. Category and Location are created if missing.";
        help.Cell(4, 1).Value = "Assigned user is a person name, not a PAV login. Unique names are linked to Users.";
        help.Cell(5, 1).Value = "Status: In Use, In Stock, Under Repair, Standby, Damaged, Lost, Retired, Disposed.";
        help.Cell(6, 1).Value = "The whole file is applied together. If any row is invalid, nothing is written.";
        help.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<ImportPreviewResponse> PreviewAsync(Stream excel, ImportColumnMap map)
    {
        var (headers, rows) = ReadSheet(excel);
        map = SidbiMap.Apply(headers, map);
        var existingSerials = await db.Assets.AsNoTracking()
            .Where(a => a.SerialNumber != null)
            .Select(a => a.SerialNumber!)
            .ToListAsync();
        var existingTags = await db.Assets.AsNoTracking().Select(a => a.AssetTag).ToListAsync();

        var tagSet = new HashSet<string>(existingTags, StringComparer.OrdinalIgnoreCase);
        var serialSet = new HashSet<string>(existingSerials, StringComparer.OrdinalIgnoreCase);
        var fileTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var preview = new ImportPreviewResponse { Columns = headers };
        foreach (var (rowNumber, values) in rows)
        {
            var prow = new ImportPreviewRow { RowNumber = rowNumber, Values = values };
            ValidateRow(prow, map, tagSet, serialSet, fileTags, fileSerials);
            preview.Rows.Add(prow);
        }

        preview.ValidCount = preview.Rows.Count(r => r.IsValid);
        preview.ErrorCount = preview.Rows.Count - preview.ValidCount;
        preview.DuplicateCount = preview.Rows.Count(r => r.Errors.Any(e => e.Contains("duplicat", StringComparison.OrdinalIgnoreCase)));
        preview.MissingIdentityCount = preview.Rows.Count(r => r.Errors.Any(e => e.Contains("Need a serial", StringComparison.OrdinalIgnoreCase)));
        preview.InvalidStatusCount = preview.Rows.Count(r => r.Errors.Any(e => e.Contains("Unknown status", StringComparison.OrdinalIgnoreCase)));
        preview.SummaryText = BuildSummary(preview);
        return preview;
    }

    private static string BuildSummary(ImportPreviewResponse preview)
    {
        if (preview.Rows.Count == 0)
            return "The file has no data rows.";
        if (preview.ErrorCount == 0)
            return $"{preview.ValidCount} row(s) ready to import. Nothing is written until you click Import.";

        var parts = new List<string>
        {
            $"{preview.ValidCount} accepted, {preview.ErrorCount} blocked. The whole file is held until every row is valid."
        };
        if (preview.MissingIdentityCount > 0)
            parts.Add($"{preview.MissingIdentityCount} missing serial / hostname / Sr No.");
        if (preview.DuplicateCount > 0)
            parts.Add($"{preview.DuplicateCount} duplicate Asset ID or serial.");
        if (preview.InvalidStatusCount > 0)
            parts.Add($"{preview.InvalidStatusCount} unknown status.");
        return string.Join(" ", parts);
    }

    public Task<ImportResult> ImportAsync(Stream excel, ImportColumnMap map, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var preview = await PreviewAsync(excel, map);
            if (preview.ErrorCount > 0)
            {
                var errors = preview.Rows
                    .Where(r => !r.IsValid)
                    .SelectMany(r => r.Errors.Select(e => $"Row {r.RowNumber}: {e}"))
                    .ToList();
                throw new AppException(400, "validation",
                    preview.SummaryText,
                    errors);
            }

            map = SidbiMap.Apply(preview.Columns, map);
            var cats = await db.Categories.ToListAsync();
            var locs = await db.Locations.ToListAsync();
            var users = (await db.Users.AsNoTracking()
                    .Select(u => new { u.Id, u.Name, u.Username })
                    .ToListAsync())
                .Select(u => (u.Id, u.Name, u.Username))
                .ToList();
            var now = DateTime.UtcNow;

            foreach (var row in preview.Rows)
            {
                var categoryName = Mapping.Clean(Get(row, map.Category)) ?? "Other";
                if (!cats.Any(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase)))
                {
                    var cat = new Category { Name = categoryName };
                    db.Categories.Add(cat);
                    cats.Add(cat);
                }

                var locName = Mapping.Clean(Get(row, map.Location));
                if (locName is not null && !locs.Any(l => l.Name.Equals(locName, StringComparison.OrdinalIgnoreCase)))
                {
                    var loc = new Location { Name = locName };
                    db.Locations.Add(loc);
                    locs.Add(loc);
                }
            }

            await SqliteGuard.SaveChangesAsync(db);

            var created = new List<Asset>();
            foreach (var row in preview.Rows)
            {
                var serial = Mapping.Clean(Get(row, map.SerialNumber));
                var hostname = Mapping.Clean(Get(row, map.Hostname));
                var sr = ParseInt(Get(row, SidbiMap.SrNo(map)));
                var tag = Mapping.Clean(Get(row, map.AssetTag))
                          ?? serial
                          ?? hostname
                          ?? (sr is { } n ? $"MUM-{n:000}" : null)
                          ?? $"PAV-{row.RowNumber}";

                var categoryName = Mapping.Clean(Get(row, map.Category)) ?? "Other";
                var cat = cats.First(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

                int? locId = null;
                var locName = Mapping.Clean(Get(row, map.Location));
                if (locName is not null)
                {
                    var loc = locs.First(l => l.Name.Equals(locName, StringComparison.OrdinalIgnoreCase));
                    locId = loc.Id;
                }

                AssetStatusNames.TryParse(Get(row, map.Status), out var status);
                if (Get(row, map.Status) is null) status = AssetStatus.InUse;

                var make = Mapping.Clean(Get(row, SidbiMap.MakeModel(map)));
                var (mfr, model) = make is null
                    ? (Mapping.Clean(Get(row, map.Manufacturer)), Mapping.Clean(Get(row, map.Model)))
                    : Mapping.SplitMakeModel(make);

                var assigned = Mapping.Clean(Get(row, map.AssignedUser));
                var asset = new Asset
                {
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now,
                    AssignedDate = assigned is null ? null : now
                };
                Mapping.Apply(asset, new SaveAssetRequest
                {
                    AssetTag = tag,
                    SrNo = sr,
                    CategoryId = cat.Id,
                    Manufacturer = mfr,
                    Model = model,
                    SerialNumber = serial,
                    Hostname = hostname,
                    IpAddress = Get(row, "IP_Address") ?? Get(row, "IP Address"),
                    LocationId = locId,
                    Status = status,
                    Designation = Get(row, "Designation"),
                    AlternateUser = Get(row, "Alternate_User") ?? Get(row, "Alternate User"),
                    Domain = Get(row, "Domain"),
                    MacAddress = Get(row, "MAC_Address") ?? Get(row, "MAC Address"),
                    Processor = Get(row, "Processor"),
                    Ram = Get(row, "RAM"),
                    Storage = Get(row, "Storage"),
                    OperatingSystem = Get(row, "OS") ?? Get(row, "Operating System"),
                    DcInstalled = Get(row, "DC_Installed") ?? Get(row, "DC Installed"),
                    AvInstalled = Get(row, "AV_Installed") ?? Get(row, "AV Installed"),
                    MsOfficeVersion = Get(row, "MS_Office_Version") ?? Get(row, "MS Office Version"),
                    MfaEnabled = Get(row, "MFA_Enabled") ?? Get(row, "MFA Enabled"),
                    IvantiInstalled = Get(row, "Ivanti_Installed") ?? Get(row, "Ivanti Installed"),
                    AdminRights = Get(row, "Admin_Rights") ?? Get(row, "Admin Rights"),
                    UsbAccess = Get(row, "USB_Access") ?? Get(row, "USB Access"),
                    ChromeUpdated = Get(row, "Chrome_Updated") ?? Get(row, "Chrome Updated"),
                    StockAvailability = Get(row, "Stock_Availability") ?? Get(row, "Stock Availability"),
                    StockWorking = Get(row, "Stock_Working") ?? Get(row, "Stock Working"),
                    PmCompleted = Get(row, "PM_Completed") ?? Get(row, "PM Completed")
                });
                UserNameResolver.ApplyTo(asset, null, assigned, users);

                db.Assets.Add(asset);
                created.Add(asset);
            }

            await SqliteGuard.SaveChangesAsync(db);

            foreach (var asset in created)
            {
                db.AssetHistory.Add(new AssetHistory
                {
                    AssetId = asset.Id,
                    Username = actor.Username,
                    Action = HistoryAction.Imported,
                    Timestamp = now
                });
            }

            await SqliteGuard.SaveChangesAsync(db);
            return new ImportResult { Imported = created.Count, Skipped = 0 };
        });

    private static void ValidateRow(
        ImportPreviewRow prow,
        ImportColumnMap map,
        HashSet<string> tagSet,
        HashSet<string> serialSet,
        HashSet<string> fileTags,
        HashSet<string> fileSerials)
    {
        var serial = Mapping.Clean(Get(prow, map.SerialNumber));
        var hostname = Mapping.Clean(Get(prow, map.Hostname));
        var sr = ParseInt(Get(prow, SidbiMap.SrNo(map)));
        var tag = Mapping.Clean(Get(prow, map.AssetTag))
                  ?? serial
                  ?? hostname
                  ?? (sr is { } n ? $"MUM-{n:000}" : null);

        if (tag is null)
            prow.Errors.Add("Need a serial number, hostname, or Sr No.");
        else if (!fileTags.Add(tag) || tagSet.Contains(tag))
            prow.Errors.Add($"Asset ID '{tag}' is duplicated.");

        if (serial is not null && (!fileSerials.Add(serial) || serialSet.Contains(serial)))
            prow.Errors.Add($"Serial number '{serial}' is duplicated.");

        var status = Get(prow, map.Status);
        if (!string.IsNullOrWhiteSpace(status) && !AssetStatusNames.TryParse(status, out _))
            prow.Errors.Add($"Unknown status '{status}'.");
    }

    private static string? Get(ImportPreviewRow row, string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return null;
        return row.Values.TryGetValue(column, out var v) ? Mapping.Clean(v) : null;
    }

    private static int? ParseInt(string? value)
    {
        if (int.TryParse(value, out var n)) return n;
        return null;
    }

    private static (List<string> headers, List<(int RowNumber, Dictionary<string, string?> Values)> rows) ReadSheet(Stream input)
    {
        if (!input.CanSeek)
        {
            var copy = new MemoryStream();
            input.CopyTo(copy);
            copy.Position = 0;
            input = copy;
        }

        var start = input.Position;
        var b0 = input.ReadByte();
        input.Position = start;
        if (b0 == 'P')
            return ReadExcel(input);

        return ReadCsv(input);
    }

    private static (List<string>, List<(int, Dictionary<string, string?>)>) ReadExcel(Stream input)
    {
        using var wb = new XLWorkbook(input);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed() ?? throw new AppException(400, "validation", "The spreadsheet is empty.");
        var headerRow = used.FirstRow();
        var colCount = used.ColumnCount();
        var headers = new List<string>();
        for (var c = 1; c <= colCount; c++)
        {
            var name = headerRow.Cell(c).GetString().Trim();
            headers.Add(string.IsNullOrWhiteSpace(name) ? $"Column{c}" : name);
        }

        var rows = new List<(int, Dictionary<string, string?>)>();
        var r = 2;
        foreach (var xlRow in used.RowsUsed().Skip(1))
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var any = false;
            for (var c = 1; c <= headers.Count; c++)
            {
                var raw = xlRow.Cell(c).GetFormattedString()?.Trim();
                if (string.IsNullOrWhiteSpace(raw)) raw = null;
                else any = true;
                values[headers[c - 1]] = raw;
            }
            if (any)
                rows.Add((r, values));
            r++;
        }
        return (headers, rows);
    }

    private static (List<string>, List<(int, Dictionary<string, string?>)>) ReadCsv(Stream input)
    {
        using var reader = new StreamReader(input, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = reader.ReadLine()
                         ?? throw new AppException(400, "validation", "The CSV file is empty.");
        var headers = ParseCsvLine(headerLine);
        if (headers.Count == 0)
            throw new AppException(400, "validation", "The CSV file has no header row.");

        var rows = new List<(int, Dictionary<string, string?>)>();
        var rowNumber = 2;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                rowNumber++;
                continue;
            }
            var cells = ParseCsvLine(line);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var any = false;
            for (var i = 0; i < headers.Count; i++)
            {
                var raw = i < cells.Count ? cells[i] : null;
                raw = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
                if (raw is not null) any = true;
                values[headers[i]] = raw;
            }
            if (any)
                rows.Add((rowNumber, values));
            rowNumber++;
        }
        return (headers, rows);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++;
                    }
                    else quoted = false;
                }
                else cur.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',')
            {
                result.Add(cur.ToString());
                cur.Clear();
            }
            else cur.Append(ch);
        }
        result.Add(cur.ToString());
        return result;
    }
}

internal static class SidbiMap
{
    public static ImportColumnMap Apply(List<string> headers, ImportColumnMap map)
    {
        string? Find(params string[] names) =>
            names.Select(n => headers.FirstOrDefault(h => h.Equals(n, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(h => h is not null);

        if (Find("Asset_Category") is { } cat)
            map.Category = cat;
        if (Find("Make_Model") is { } make)
        {
            map.Manufacturer = "";
            map.Model = "";
        }
        if (Find("Serial_Number", "Serial Number") is { } sn)
            map.SerialNumber = sn;
        if (Find("User Name", "UserName", "Assigned User") is { } user)
            map.AssignedUser = user;
        if (Find("Sr No", "SrNo", "Asset ID") is { } sr && string.IsNullOrWhiteSpace(map.AssetTag))
            map.AssetTag = "";
        if (Find("Location") is { } loc)
            map.Location = loc;
        if (Find("Status") is { } st)
            map.Status = st;
        if (Find("Hostname") is { } host)
            map.Hostname = host;
        return map;
    }

    public static string SrNo(ImportColumnMap map) =>
        string.IsNullOrWhiteSpace(map.AssetTag) ? "Sr No" : "Sr No";

    public static string MakeModel(ImportColumnMap _) => "Make_Model";
}
