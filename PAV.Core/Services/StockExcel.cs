using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PAV.Shared.Dtos;

namespace PAV.Core.Services;

/// <summary>
/// Reads the SIDBI consumable workbooks. 2026 unit-list is opening stock.
/// 2025 historical/planning sheets are not imported (would double-count).
/// </summary>
public static class StockExcel
{
    public static readonly string[] Categories =
    [
        "Peripheral", "Cable", "Adapter", "Battery", "Monitor", "Network",
        "Storage", "Toner", "Consumable", "Tool", "Other"
    ];

    public static string NormalizeKey(string? name, string? manufacturer = null, string? model = null)
    {
        var raw = string.Join(" ", new[] { name, manufacturer, model }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var collapsed = Regex.Replace(raw.Trim().ToLowerInvariant(), @"\s+", " ");
        return collapsed;
    }

    public static StockImportPreviewDto Preview(
        Stream stream,
        string fileName,
        IReadOnlyList<(int Id, string Name, string Key)> existingStock,
        IReadOnlyList<(int Id, string Name, string Username)> users)
    {
        using var wb = new XLWorkbook(stream);
        var preview = new StockImportPreviewDto { SourceName = fileName };

        var names = wb.Worksheets.Select(s => s.Name).ToList();
        var historical = names.Any(n =>
            n.Contains("New Laptop", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Printer list", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Printer catrage", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Printer cartridge", StringComparison.OrdinalIgnoreCase));

        if (historical)
        {
            preview.SourceKind = "2025-historical";
            preview.CanImport = false;
            preview.SkippedSheets = names;
            preview.Issues.Add(
                "This looks like Consumable Stock Details 2025.xlsx (laptops, printers, historical issues, purchase lists).");
            preview.Issues.Add(
                "It is not current opening stock. Import Consumable Stock Details 2026.xlsx instead so 2025 and 2026 are not double-counted.");
            preview.Summary = "Rejected: historical 2025 workbook. Use the 2026 stock file.";
            return preview;
        }

        var sheet = FindUnitSheet(wb);
        if (sheet is null)
        {
            preview.SourceKind = "unknown";
            preview.CanImport = false;
            preview.SkippedSheets = names;
            preview.Issues.Add("No sheet with columns Sr.No / Make & Model was found.");
            preview.Summary = "Could not read this workbook as stock.";
            return preview;
        }

        var rows = ReadUnitRows(sheet);
        if (rows.Count == 0)
        {
            preview.SourceKind = "empty";
            preview.CanImport = false;
            preview.Issues.Add("The stock sheet has no product rows.");
            preview.Summary = "No rows to import.";
            return preview;
        }

        var issuedShare = rows.Count(r => r.IsIssued) / (double)rows.Count;
        if (issuedShare > 0.4 && rows.Count > 50)
        {
            preview.SourceKind = "2025-issue-ledger";
            preview.CanImport = false;
            preview.Issues.Add(
                $"{rows.Count(r => r.IsIssued)} of {rows.Count} rows are already issued. That is an issue ledger, not current shelf stock.");
            preview.Issues.Add("Do not import it as opening balance. Use the 2026 workbook.");
            preview.Summary = "Rejected: looks like a 2025 issue ledger.";
            return preview;
        }

        preview.SourceKind = "2026-unit-list";
        var grouped = rows.GroupBy(r => NormalizeKey(r.MakeModel)).ToList();
        var existingByKey = existingStock
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var g in grouped)
        {
            var first = g.First();
            var parsed = ParseProduct(first.MakeModel);
            var classification = Classify(parsed.Name, parsed.Manufacturer, parsed.Model);
            var issued = g.Where(x => x.IsIssued).ToList();
            var line = new StockImportLineDto
            {
                Name = parsed.Name,
                Manufacturer = parsed.Manufacturer,
                Model = parsed.Model,
                Category = parsed.Category,
                OpeningQty = g.Count(),
                IssuedQty = issued.Count,
                Classification = classification,
                Action = "Create"
            };

            if (classification == "Ambiguous")
            {
                line.Notes = AmbiguousReason(parsed.Name);
                preview.AmbiguousItems++;
                preview.ReviewRows++;
            }

            existingByKey.TryGetValue(g.Key, out var hits);
            hits ??= [];
            if (hits.Count == 1)
            {
                line.Action = "SkipExisting";
                line.Issues.Add($"Already in PAV as '{hits[0].Name}'. Will not add another opening balance (avoids double-count).");
                preview.ExistingMatched++;
            }
            else if (hits.Count > 1)
            {
                line.Action = "AmbiguousMatch";
                line.Issues.Add($"Matches {hits.Count} existing stock items. Will not merge.");
                preview.ExistingMatched++;
                preview.ReviewRows++;
            }
            else
            {
                preview.NewItems++;
                preview.OpeningUnits += line.OpeningQty;
                preview.IssueRows += line.IssuedQty;
            }

            foreach (var iss in issued)
            {
                if (!string.IsNullOrWhiteSpace(iss.User) &&
                    UserNameResolver.ResolveUniqueId(users, iss.User) is null)
                {
                    line.Issues.Add($"Issued to '{iss.User}' — no unique PAV user match. Name will be stored as text.");
                }
            }

            preview.Lines.Add(line);
        }

        var skip = preview.Lines.Count(l => l.Action is "SkipExisting" or "AmbiguousMatch");
        preview.CanImport = preview.NewItems > 0;
        preview.Summary = preview.CanImport
            ? $"{preview.NewItems} new items, {preview.OpeningUnits} opening units, {preview.IssueRows} already issued, {preview.AmbiguousItems} need review, {skip} skipped as existing."
            : skip > 0
                ? "Every product already exists. Import would double-count — nothing will be written."
                : "Nothing to import.";

        if (preview.AmbiguousItems > 0)
            preview.Issues.Add(
                "Monitors / the Brother label machine are stored as stock quantity (2026 has no serials). Move to Inventory later if you tag them.");

        return preview;
    }

    public static List<ParsedStockGroup> GroupsForImport(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var sheet = FindUnitSheet(wb) ?? throw new AppException(400, "validation", "No stock sheet found.");
        var rows = ReadUnitRows(sheet);
        var list = new List<ParsedStockGroup>();
        foreach (var g in rows.GroupBy(r => NormalizeKey(r.MakeModel)))
        {
            var first = g.First();
            var parsed = ParseProduct(first.MakeModel);
            list.Add(new ParsedStockGroup
            {
                Key = g.Key,
                Name = parsed.Name,
                Manufacturer = parsed.Manufacturer,
                Model = parsed.Model,
                Category = parsed.Category,
                Classification = Classify(parsed.Name, parsed.Manufacturer, parsed.Model),
                Rows = g.ToList()
            });
        }
        return list;
    }

    private static IXLWorksheet? FindUnitSheet(XLWorkbook wb)
    {
        foreach (var ws in wb.Worksheets)
        {
            var map = Headers(ws);
            if (map.ContainsKey("make & model") || map.ContainsKey("make and model"))
                return ws;
        }
        return wb.Worksheets.FirstOrDefault();
    }

    private static Dictionary<string, int> Headers(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var row = ws.Row(1);
        for (var c = 1; c <= Math.Min(16, row.LastCellUsed()?.Address.ColumnNumber ?? 10); c++)
        {
            var text = Clean(row.Cell(c).GetString());
            if (text != null)
                map[text.ToLowerInvariant()] = c;
        }
        return map;
    }

    private static List<UnitRow> ReadUnitRows(IXLWorksheet ws)
    {
        var map = Headers(ws);
        int Col(params string[] keys)
        {
            foreach (var k in keys)
                if (map.TryGetValue(k, out var i)) return i;
            return 0;
        }

        var makeCol = Col("make & model", "make and model");
        var serialCol = Col("serialnumber", "serial number", "serial");
        var statusCol = Col("status");
        var userCol = Col("username", "user name", "user");
        var dateCol = Col("date");
        var qtyCol = Col("quantity");
        var remarksCol = Col("additional remarks", "remarks");
        var productCol = Col("name of product", "product");
        var srCol = Col("sr.no", "sr no", "sr.no.", "s.no", "sl");

        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        var rows = new List<UnitRow>();
        for (var r = 2; r <= last; r++)
        {
            var make = Clean(ws.Cell(r, makeCol == 0 ? 2 : makeCol).GetString());
            if (make is null) continue;
            var status = Clean(ws.Cell(r, statusCol).GetString());
            var user = userCol == 0 ? null : Clean(ws.Cell(r, userCol).GetString());
            var serial = serialCol == 0 ? null : Clean(ws.Cell(r, serialCol).GetString());
            DateTime? date = null;
            if (dateCol > 0)
            {
                var cell = ws.Cell(r, dateCol);
                if (cell.TryGetValue(out DateTime dt)) date = dt;
                else if (DateTime.TryParse(cell.GetString(), out var parsed)) date = parsed;
            }

            int? qty = null;
            if (qtyCol > 0 && ws.Cell(r, qtyCol).TryGetValue(out double q) && q != 0)
                qty = (int)q;

            var issued = IsIssued(status, user);
            rows.Add(new UnitRow
            {
                Sr = srCol == 0 ? r - 1 : (ws.Cell(r, srCol).TryGetValue(out double sr) ? (int)sr : r - 1),
                MakeModel = make,
                Serial = serial,
                Status = status,
                User = user,
                Date = date,
                Qty = qty,
                Remarks = remarksCol == 0 ? null : Clean(ws.Cell(r, remarksCol).GetString()),
                Product = productCol == 0 ? null : Clean(ws.Cell(r, productCol).GetString()),
                IsIssued = issued
            });
        }
        return rows;
    }

    private static bool IsIssued(string? status, string? user)
    {
        var s = (status ?? "").ToLowerInvariant();
        if (s.Contains("provided") || s.Contains("issued") || s == "out")
            return true;
        return !string.IsNullOrWhiteSpace(user);
    }

    public static (string Name, string? Manufacturer, string? Model, string Category) ParseProduct(string makeModel)
    {
        var name = Regex.Replace(makeModel.Trim(), @"\s+", " ");
        string? manufacturer = null;
        foreach (var brand in new[] { "Dell", "HP", "Portronics", "Brother", "Chillmate", "Agro", "Poly", "Logitech", "TP Link", "TP-Link", "Acer", "Honeywell", "Crucial" })
        {
            if (name.StartsWith(brand, StringComparison.OrdinalIgnoreCase))
            {
                manufacturer = brand.Equals("TP-Link", StringComparison.OrdinalIgnoreCase) ? "TP-Link" : brand;
                break;
            }
        }

        string? model = null;
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens.Reverse())
        {
            if (Regex.IsMatch(t, @"^[A-Z]{1,4}\d{3,}", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(t, @"^[A-Z]?\d+[A-Z]?\d*", RegexOptions.IgnoreCase) && t.Length >= 5 ||
                Regex.IsMatch(t, @"^M\d{4,}-\d+", RegexOptions.IgnoreCase) ||
                t.Contains("Tze", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("KM", StringComparison.OrdinalIgnoreCase) && t.Length > 4)
            {
                model = t.Trim(',');
                break;
            }
        }

        var cat = GuessCategory(name);
        return (name, manufacturer, model, cat);
    }

    public static string GuessCategory(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("toner") || n.Contains("cartridge")) return "Toner";
        if (n.Contains("battery")) return "Battery";
        if (n.Contains("monitor") || n.Contains("led")) return "Monitor";
        if (n.Contains("switch") || n.Contains("router")) return "Network";
        if (n.Contains("ssd") || n.Contains("storage")) return "Storage";
        if (n.Contains("tape") && n.Contains("tze") || n.Contains("label") && n.Contains("tape")) return "Consumable";
        if (n.Contains("adapter") || n.Contains("adaptor") || n.Contains("charger")) return "Adapter";
        if (n.Contains("cable") || n.Contains("hdmi") || n.Contains("vga") || n.Contains("convertor") || n.Contains("converter")) return "Cable";
        if (n.Contains("screwdriver") || n.Contains("tester") || n.Contains("p touch") || n.Contains("label")) return "Tool";
        if (n.Contains("mouse") || n.Contains("keyboard") || n.Contains("k/m") || n.Contains("speaker") || n.Contains("headset") || n.Contains("cooler")) return "Peripheral";
        return "Other";
    }

    public static string Classify(string name, string? manufacturer, string? model)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("laptop") && !n.Contains("cooler") && !n.Contains("battery") && !n.Contains("charger") && !n.Contains("adapter") && !n.Contains("stand"))
            return "SerializedAsset";
        if (Regex.IsMatch(n, @"\bprinter\b") && !n.Contains("label") && !n.Contains("p touch"))
            return "SerializedAsset";
        if (n.Contains("monitor") || n.Contains("p touch") || n.Contains("label asset tagging machine") || n.Contains("label printer"))
            return "Ambiguous";
        return "Stock";
    }

    public static string AmbiguousReason(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("monitor"))
            return "Monitor — 2026 has quantity rows and no serials. Kept as stock. Move to Inventory if you tag each unit.";
        if (n.Contains("p touch") || n.Contains("label"))
            return "Label machine — one unit, no serial in 2026. Kept as stock quantity of 1.";
        return "Could be stock or a serialized asset. Imported as stock; review.";
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Replace('\u00a0', ' ').Trim();
        return t.Length == 0 ? null : t;
    }

    public sealed class UnitRow
    {
        public int Sr { get; set; }
        public string MakeModel { get; set; } = "";
        public string? Serial { get; set; }
        public string? Status { get; set; }
        public string? User { get; set; }
        public DateTime? Date { get; set; }
        public int? Qty { get; set; }
        public string? Remarks { get; set; }
        public string? Product { get; set; }
        public bool IsIssued { get; set; }
    }

    public sealed class ParsedStockGroup
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string Category { get; set; } = "Other";
        public string Classification { get; set; } = "Stock";
        public List<UnitRow> Rows { get; set; } = [];
    }
}
