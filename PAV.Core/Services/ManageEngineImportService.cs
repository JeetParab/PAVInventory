using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Core.Services;

public class ManageEngineImportService(AppDbContext db, IWriteLock writeLock)
{
    public async Task<MeImportPreviewDto> PreviewAsync(Stream excel) =>
        await BuildPlanAsync(excel);

    public Task<MeImportResultDto> ImportAsync(Stream excel, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var plan = await BuildPlanAsync(excel);
            if (!plan.CanImport)
                throw new AppException(400, "validation", plan.Summary, plan.Issues);

            var cats = await db.Categories.AsNoTracking().ToListAsync();
            var laptop = cats.FirstOrDefault(c => c.Name.Equals("Laptop", StringComparison.OrdinalIgnoreCase))?.Id
                         ?? cats[0].Id;
            var desktop = cats.FirstOrDefault(c => c.Name.Equals("Desktop", StringComparison.OrdinalIgnoreCase))?.Id
                          ?? laptop;
            var aio = cats.FirstOrDefault(c => c.Name.Equals("All-in-One", StringComparison.OrdinalIgnoreCase))?.Id
                      ?? desktop;

            var updated = 0;
            var created = 0;
            foreach (var work in plan.Work)
            {
                if (work.Action == "Skip") continue;
                if (work.Action == "Update" && work.AssetId is { } id)
                {
                    var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id);
                    if (asset is null) continue;
                    var oldIp = asset.IpAddress;
                    ApplyLive(asset, work, fillSerial: string.IsNullOrWhiteSpace(asset.SerialNumber));
                    asset.Version++;
                    asset.UpdatedAt = DateTime.UtcNow;
                    if (work.History.Count > 0)
                    {
                        foreach (var h in work.History)
                        {
                            h.AssetId = asset.Id;
                            h.Username = actor.Username;
                            h.Timestamp = DateTime.UtcNow;
                            h.Action = HistoryAction.Imported;
                        }
                        db.AssetHistory.AddRange(work.History);
                    }
                    await SqliteGuard.SaveChangesAsync(db);
                    await IpAssetBridge.AfterAssetSavedAsync(db, asset, oldIp, actor);
                    updated++;
                }
                else if (work.Action == "New")
                {
                    var now = DateTime.UtcNow;
                    var tag = await UniqueTagAsync(work.Serial ?? work.Hostname ?? "ME");
                    var asset = new Asset
                    {
                        AssetTag = tag,
                        CategoryId = work.ComputerType switch
                        {
                            "Desktop" => desktop,
                            "All in One" or "All-in-One" => aio,
                            _ => laptop
                        },
                        Status = AssetStatus.InUse,
                        NeedsReview = true,
                        Version = 1,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    ApplyLive(asset, work, fillSerial: true);
                    db.Assets.Add(asset);
                    await SqliteGuard.SaveChangesAsync(db);
                    db.AssetHistory.Add(new AssetHistory
                    {
                        AssetId = asset.Id,
                        Username = actor.Username,
                        Action = HistoryAction.Imported,
                        FieldName = "ManageEngine",
                        NewValue = "New from ManageEngine — confirm in Pending",
                        Timestamp = now
                    });
                    await SqliteGuard.SaveChangesAsync(db);
                    await IpAssetBridge.AfterAssetSavedAsync(db, asset, previousIp: null, actor);
                    created++;
                }
            }

            return new MeImportResultDto
            {
                Updated = updated,
                Created = created,
                Summary = $"{updated} updated, {created} new (Pending confirm)."
            };
        });

    private sealed class WorkRow
    {
        public string Action { get; set; } = "";
        public string MatchBy { get; set; } = "";
        public int? AssetId { get; set; }
        public string? AssetTag { get; set; }
        public string? Serial { get; set; }
        public string? Hostname { get; set; }
        public string? Mac { get; set; }
        public string? Ip { get; set; }
        public bool ApplyIp { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? Os { get; set; }
        public string? Domain { get; set; }
        public string? Ram { get; set; }
        public DateTime? LastConnected { get; set; }
        public string? MeLogon { get; set; }
        public string ComputerType { get; set; } = "";
        public string Changes { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<AssetHistory> History { get; } = [];
    }

    private sealed class MeImportPreviewDtoInternal : MeImportPreviewDto
    {
        public List<WorkRow> Work { get; set; } = [];
    }

    private async Task<MeImportPreviewDtoInternal> BuildPlanAsync(Stream excel)
    {
        var parsed = Parse(excel);
        var preview = new MeImportPreviewDtoInternal();
        if (parsed.Count == 0)
        {
            preview.Summary = "No computer rows found. Export Computers Summary from ManageEngine Endpoint Central.";
            preview.Issues.Add("Expected columns: Computer Name, Service Tag/Serial Number, MAC Address, IP Address.");
            return preview;
        }

        var assets = await db.Assets.AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.AssetTag,
                a.SerialNumber,
                a.Hostname,
                a.MacAddress,
                a.IpAddress,
                a.Manufacturer,
                a.Model,
                a.OperatingSystem,
                a.Domain,
                a.Ram,
                a.LastConnected,
                a.MeLogon
            })
            .ToListAsync();

        var bySerial = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var byMac = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var byHost = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var hostNameOf = assets.ToDictionary(a => a.Id, a => a.Hostname);
        foreach (var a in assets)
        {
            if (NormSerial(a.SerialNumber) is { } s)
                Add(bySerial, s, a.Id);
            if (NormMac(a.MacAddress) is { } m)
                Add(byMac, m, a.Id);
            if (NormHost(a.Hostname) is { } h)
                Add(byHost, h, a.Id);
        }

        var ipGroups = parsed
            .Where(p => p.FloorIp is not null)
            .GroupBy(p => p.FloorIp!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastContact ?? DateTime.MinValue).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var work = new List<WorkRow>();
        foreach (var row in parsed)
        {
            var w = new WorkRow
            {
                Serial = row.Serial,
                Hostname = row.Hostname,
                Mac = row.Mac,
                Ip = row.FloorIp,
                Manufacturer = row.Manufacturer,
                Model = row.Model,
                Os = row.Os,
                Domain = row.Domain,
                Ram = row.Ram,
                LastConnected = row.LastContact,
                MeLogon = row.MeLogon,
                ComputerType = row.ComputerType
            };

            var notes = new List<string>();
            if (row.FloorIp is { } ip && ipGroups.TryGetValue(ip, out var group) && group.Count > 1)
            {
                var winner = group[0];
                w.ApplyIp = ReferenceEquals(winner, row);
                if (!w.ApplyIp)
                    notes.Add($"IP {ip} kept on newer scan {group[0].Hostname}");
            }
            else
                w.ApplyIp = row.FloorIp is not null;

            int? matchId = null;
            if (row.Serial is { } serial && bySerial.TryGetValue(serial, out var sids) && sids.Count == 1)
            {
                matchId = sids[0];
                w.MatchBy = "Serial";
            }
            else if (row.Serial is null && row.Mac is { } mac && byMac.TryGetValue(mac, out var mids) && mids.Count == 1)
            {
                matchId = mids[0];
                w.MatchBy = "MAC";
            }
            else if (row.Serial is null && row.Hostname is { } host && byHost.TryGetValue(host, out var hids) && hids.Count == 1)
            {
                matchId = hids[0];
                w.MatchBy = "Hostname";
            }

            if (row.Hostname is { } hn)
            {
                if (byHost.TryGetValue(hn, out var clash) && clash.Any(id => id != matchId))
                    notes.Add("Hostname already on another PAV asset");
            }

            if (matchId is { } id)
            {
                var pav = assets.First(a => a.Id == id);
                w.Action = "Update";
                w.AssetId = id;
                w.AssetTag = pav.AssetTag;
                var changes = new List<string>();
                Track(w, changes, "Hostname", pav.Hostname, w.Hostname);
                if (w.ApplyIp)
                    Track(w, changes, "IP Address", pav.IpAddress, w.Ip);
                else if (string.Equals(pav.IpAddress, w.Ip, StringComparison.OrdinalIgnoreCase))
                {
                    Track(w, changes, "IP Address", pav.IpAddress, "");
                    w.Ip = null;
                    w.ApplyIp = true;
                }
                Track(w, changes, "MAC Address", pav.MacAddress, w.Mac);
                if (string.IsNullOrWhiteSpace(pav.SerialNumber) && w.Serial is not null)
                    Track(w, changes, "Serial Number", pav.SerialNumber, w.Serial);
                Track(w, changes, "Manufacturer", pav.Manufacturer, w.Manufacturer);
                Track(w, changes, "Model", pav.Model, w.Model);
                Track(w, changes, "OS", pav.OperatingSystem, w.Os);
                Track(w, changes, "Domain", pav.Domain, w.Domain);
                Track(w, changes, "RAM", pav.Ram, w.Ram);
                if (w.LastConnected is { } lc && pav.LastConnected != lc)
                    changes.Add("Last connected");
                if (!string.Equals(pav.MeLogon, w.MeLogon, StringComparison.OrdinalIgnoreCase) && w.MeLogon is not null)
                    changes.Add("ME logon");
                w.Changes = changes.Count == 0 ? "(no field changes)" : string.Join(", ", changes);
            }
            else if (row.Hostname is null && row.Serial is null && row.Mac is null)
            {
                w.Action = "Skip";
                notes.Add("No serial, MAC or hostname");
            }
            else
            {
                w.Action = "New";
                w.MatchBy = "";
                w.Changes = "Create — confirm in Pending";
                notes.Add("Not in PAV");
            }

            w.Notes = string.Join("; ", notes);
            work.Add(w);
        }

        preview.Work = work;
        preview.UpdateCount = work.Count(x => x.Action == "Update");
        preview.NewCount = work.Count(x => x.Action == "New");
        preview.SkipCount = work.Count(x => x.Action == "Skip");
        preview.CanImport = work.Exists(x => x.Action is "Update" or "New");
        preview.Summary =
            $"{parsed.Count} ME computers: {preview.UpdateCount} update, {preview.NewCount} new (Pending confirm), {preview.SkipCount} skip. " +
            "Serial is the match key. Hostname/IP/MAC follow ME. New PCs wait on Pending. Users are not assigned.";
        if (work.Exists(x => x.Notes.Contains("IP ", StringComparison.Ordinal)))
            preview.Issues.Add("Some IPs appear on more than one ME row. The newest Last Contact keeps the address.");
        if (work.Exists(x => x.Notes.Contains("Hostname already", StringComparison.Ordinal)))
            preview.Issues.Add("Some hostnames already exist on a different PAV serial. ME hostname is still applied to the matched serial.");

        preview.Lines = work.Select(x => new MeImportLineDto
        {
            Action = x.Action,
            MatchBy = x.MatchBy,
            AssetTag = x.AssetTag,
            Serial = x.Serial,
            Hostname = x.Hostname,
            IpAddress = x.ApplyIp ? x.Ip : "",
            Model = x.Model,
            MeLogon = x.MeLogon,
            Changes = x.Changes,
            Notes = x.Notes
        }).ToList();
        return preview;
    }

    private static void Track(WorkRow w, List<string> changes, string field, string? oldVal, string? newVal)
    {
        oldVal = string.IsNullOrWhiteSpace(oldVal) ? null : oldVal.Trim();
        newVal = string.IsNullOrWhiteSpace(newVal) ? null : newVal.Trim();
        if (string.Equals(oldVal, newVal, StringComparison.OrdinalIgnoreCase))
            return;
        if (newVal is null && field is "Manufacturer" or "Model" or "OS" or "Domain" or "RAM" or "MAC Address")
            return;
        changes.Add(field);
        w.History.Add(new AssetHistory
        {
            FieldName = field,
            OldValue = oldVal ?? "",
            NewValue = newVal ?? ""
        });
    }

    private static void ApplyLive(Asset asset, WorkRow w, bool fillSerial)
    {
        if (w.Hostname is not null) asset.Hostname = w.Hostname;
        if (w.ApplyIp)
            asset.IpAddress = w.Ip;
        if (w.Mac is not null) asset.MacAddress = w.Mac;
        if (fillSerial && w.Serial is not null) asset.SerialNumber = w.Serial;
        if (w.Manufacturer is not null) asset.Manufacturer = w.Manufacturer;
        if (w.Model is not null) asset.Model = w.Model;
        if (w.Os is not null) asset.OperatingSystem = w.Os;
        if (w.Domain is not null) asset.Domain = w.Domain;
        if (w.Ram is not null) asset.Ram = w.Ram;
        if (w.LastConnected is { } lc) asset.LastConnected = lc;
        if (w.MeLogon is not null) asset.MeLogon = w.MeLogon;
    }

    private async Task<string> UniqueTagAsync(string raw)
    {
        var baseTag = Mapping.Clean(raw) ?? "ME";
        if (baseTag.Length > 120) baseTag = baseTag[..120];
        var tag = baseTag;
        var n = 2;
        while (await db.Assets.AnyAsync(a => a.AssetTag.ToLower() == tag.ToLower()))
        {
            tag = baseTag + "-" + n;
            n++;
        }
        return tag;
    }

    private sealed class ParsedRow
    {
        public string? Hostname { get; set; }
        public string? Serial { get; set; }
        public string? Mac { get; set; }
        public string? FloorIp { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string ComputerType { get; set; } = "";
        public string? Os { get; set; }
        public string? Domain { get; set; }
        public string? Ram { get; set; }
        public DateTime? LastContact { get; set; }
        public string? MeLogon { get; set; }
    }

    private static List<ParsedRow> Parse(Stream excel)
    {
        using var wb = new XLWorkbook(excel);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed() ?? throw new AppException(400, "validation", "The spreadsheet is empty.");
        var headerRow = used.FirstRow();
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var name = cell.GetString().Trim();
            if (name.Length > 0 && !map.ContainsKey(name))
                map[name] = cell.Address.ColumnNumber;
        }

        int? Col(params string[] names)
        {
            foreach (var n in names)
            {
                foreach (var kv in map)
                {
                    if (kv.Key.Equals(n, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                    if (kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                }
            }
            return null;
        }

        var cHost = Col("Computer Name") ?? throw new AppException(400, "validation",
            "This is not a ManageEngine Computers Summary. Need a Computer Name column.");
        var cSerial = Col("Service Tag/Serial Number", "Service Tag", "Serial Number");
        var cMac = Col("MAC Address");
        var cIp = Col("IP Address");
        var cModel = Col("Device Model");
        var cType = Col("Computer Type");
        var cOs = Col("Operating System");
        var cDomain = Col("Domain");
        var cRam = Col("Physical Memory");
        var cContact = Col("Last Contact Time");
        var cLogged = Col("Logged On Users");
        var cLast = Col("Last Logon User");

        string? Cell(IXLRangeRow row, int? col)
        {
            if (col is null) return null;
            var v = row.Cell(col.Value);
            if (v.DataType == XLDataType.DateTime)
                return v.GetDateTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
            var s = v.GetFormattedString().Trim();
            return Junk(s);
        }

        var rows = new List<ParsedRow>();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            var host = Cell(row, cHost);
            var serial = NormSerial(Cell(row, cSerial));
            var mac = NormMac(Cell(row, cMac));
            if (host is null && serial is null && mac is null) continue;
            SplitMake(Cell(row, cModel), out var mfr, out var model);
            var ramRaw = Cell(row, cRam);
            var ram = ramRaw is null ? null : ramRaw.Contains("GB", StringComparison.OrdinalIgnoreCase) ? ramRaw : ramRaw + " GB";
            var logged = Cell(row, cLogged);
            var last = Cell(row, cLast);
            rows.Add(new ParsedRow
            {
                Hostname = host,
                Serial = serial,
                Mac = mac,
                FloorIp = FloorIp(Cell(row, cIp)),
                Manufacturer = mfr,
                Model = model,
                ComputerType = Cell(row, cType) ?? "",
                Os = Cell(row, cOs),
                Domain = Cell(row, cDomain),
                Ram = ram,
                LastContact = ParseDate(row, cContact),
                MeLogon = PickLogon(logged, last)
            });
        }
        return rows;
    }

    private static string? PickLogon(string? logged, string? last)
    {
        var raw = logged ?? last;
        if (raw is null) return null;
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.Equals("Admin", StringComparison.OrdinalIgnoreCase) || p is "-" or "'-")
                continue;
            return p;
        }
        return parts.FirstOrDefault();
    }

    private static DateTime? ParseDate(IXLRangeRow row, int? col)
    {
        if (col is null) return null;
        var cell = row.Cell(col.Value);
        if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime();
        var s = cell.GetFormattedString().Trim();
        if (s.Length == 0) return null;
        var formats = new[]
        {
            "MMM d, yyyy h:mm tt", "MMM dd, yyyy hh:mm tt", "MMM d, yyyy hh:mm tt",
            "MMMM d, yyyy h:mm tt", "M/d/yyyy h:mm tt", "dd-MM-yyyy HH:mm"
        };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;
        return null;
    }

    private static string? FloorIp(string? raw)
    {
        if (raw is null) return null;
        foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IpAddressService.TryNormalize(part, out var ip, out var third, out _) && third is >= 101 and <= 107)
                return ip;
        }
        return null;
    }

    private static void SplitMake(string? device, out string? mfr, out string? model)
    {
        mfr = null;
        model = Junk(device);
        if (model is null) return;
        if (model.StartsWith("HP", StringComparison.OrdinalIgnoreCase))
        {
            mfr = "HP";
            return;
        }
        if (model.Contains("TravelLite", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("Veriton", StringComparison.OrdinalIgnoreCase))
        {
            mfr = "Acer";
            return;
        }
        var sp = model.IndexOf(' ');
        mfr = sp > 0 ? model[..sp] : model;
    }

    private static string? Junk(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (s is "-" or "'-" or "' -" or ".") return null;
        return s;
    }

    private static string? NormSerial(string? s)
    {
        s = Junk(s);
        return s?.ToUpperInvariant();
    }

    private static string? NormMac(string? s)
    {
        s = Junk(s);
        if (s is null) return null;
        var hex = new string(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return hex.Length >= 8 ? hex : null;
    }

    private static string? NormHost(string? s) => Junk(s)?.ToUpperInvariant();

    private static void Add(Dictionary<string, List<int>> map, string key, int id)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }
        list.Add(id);
    }
}
