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
                "This is the original 2025 workbook (laptops, printers, issue ledger, purchase lists mixed together).");
            preview.Issues.Add(
                "Do not import it. Use Consumable Stock Details 2026.xlsx for new stock, then PAV-Stock-2025-Remaining.xlsx for leftover 2025 quantity that is not in 2026.");
            preview.Summary = "Rejected: original 2025 workbook. Use the cleaned remaining file.";
            return preview;
        }

        var summarySheet = FindSummarySheet(wb);
        if (summarySheet is not null)
        {
            var groups = ReadSummaryGroups(summarySheet);
            return FillPreview(preview, groups, existingStock, users, "summary-opening");
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
        var grouped = rows.GroupBy(r => NormalizeKey(r.MakeModel))
            .Select(g =>
            {
                var first = g.First();
                var parsed = ParseProduct(first.MakeModel);
                return new ParsedStockGroup
                {
                    Key = g.Key,
                    Name = parsed.Name,
                    Manufacturer = parsed.Manufacturer,
                    Model = parsed.Model,
                    Category = parsed.Category,
                    Classification = Classify(parsed.Name, parsed.Manufacturer, parsed.Model),
                    OpeningQty = g.Count(),
                    Rows = g.ToList()
                };
            })
            .ToList();
        return FillPreview(preview, grouped, existingStock, users, "2026-unit-list");
    }

    public static List<ParsedStockGroup> GroupsForImport(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var names = wb.Worksheets.Select(s => s.Name).ToList();
        if (names.Any(n =>
                n.Contains("New Laptop", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Printer list", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Printer catrage", StringComparison.OrdinalIgnoreCase)))
            throw new AppException(400, "validation", "Original 2025 workbook cannot be imported. Use PAV-Stock-2025-Remaining.xlsx.");

        var summary = FindSummarySheet(wb);
        if (summary is not null)
            return ReadSummaryGroups(summary);

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
                OpeningQty = g.Count(),
                Rows = g.ToList()
            });
        }
        return list;
    }

    private static StockImportPreviewDto FillPreview(
        StockImportPreviewDto preview,
        List<ParsedStockGroup> grouped,
        IReadOnlyList<(int Id, string Name, string Key)> existingStock,
        IReadOnlyList<(int Id, string Name, string Username)> users,
        string kind)
    {
        preview.SourceKind = kind;
        var existingByKey = existingStock
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var g in grouped)
        {
            if (g.Classification == "SerializedAsset")
            {
                preview.Lines.Add(new StockImportLineDto
                {
                    Name = g.Name,
                    Manufacturer = g.Manufacturer,
                    Model = g.Model,
                    Category = g.Category,
                    OpeningQty = g.OpeningQty,
                    Classification = g.Classification,
                    Action = "SkipSerialized",
                    Notes = "Looks like a serialized asset. Not imported as stock."
                });
                preview.ReviewRows++;
                continue;
            }

            var issued = g.Rows.Where(x => x.IsIssued).ToList();
            var line = new StockImportLineDto
            {
                Name = g.Name,
                Manufacturer = g.Manufacturer,
                Model = g.Model,
                Category = g.Category,
                OpeningQty = g.OpeningQty,
                IssuedQty = issued.Count,
                Classification = g.Classification,
                Action = "Create",
                Notes = g.Notes
            };

            if (g.Classification == "Ambiguous")
            {
                line.Notes = string.Join(" ", new[] { line.Notes, AmbiguousReason(g.Name) }.Where(s => !string.IsNullOrWhiteSpace(s)));
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

        var skip = preview.Lines.Count(l => l.Action is "SkipExisting" or "AmbiguousMatch" or "SkipSerialized");
        preview.CanImport = preview.NewItems > 0;
        preview.Summary = preview.CanImport
            ? $"{preview.NewItems} new items, {preview.OpeningUnits} opening units, {preview.IssueRows} already issued, {preview.AmbiguousItems} need review, {skip} skipped."
            : skip > 0
                ? "Every product already exists or was skipped. Nothing will be written."
                : "Nothing to import.";

        if (kind == "summary-opening")
            preview.Issues.Add("Summary file: one row per product, Quantity = opening balance. Walk the cupboard before you trust old remaining numbers.");

        if (preview.AmbiguousItems > 0)
            preview.Issues.Add(
                "Some items may belong in Inventory if they have serials (monitors, routers, label machines). They are still imported as stock quantity.");

        return preview;
    }

    private static IXLWorksheet? FindSummarySheet(XLWorkbook wb)
    {
        foreach (var name in new[] { "OpeningStock", "Stock", "Remaining" })
        {
            var hit = wb.Worksheets.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }

        foreach (var ws in wb.Worksheets)
        {
            if (ws.Name.StartsWith("DoNotImport", StringComparison.OrdinalIgnoreCase)) continue;
            if (ws.Name.Equals("README", StringComparison.OrdinalIgnoreCase)) continue;
            var map = Headers(ws);
            var hasQty = map.ContainsKey("quantity") || map.ContainsKey("qty") || map.ContainsKey("opening");
            var hasName = map.ContainsKey("name") || map.ContainsKey("item");
            var unitList = map.ContainsKey("make & model") || map.ContainsKey("make and model");
            if (hasQty && hasName && !unitList)
                return ws;
        }
        return null;
    }

    private static List<ParsedStockGroup> ReadSummaryGroups(IXLWorksheet ws)
    {
        var map = Headers(ws);
        int Col(params string[] keys)
        {
            foreach (var k in keys)
                if (map.TryGetValue(k, out var i)) return i;
            return 0;
        }
        var nameCol = Col("name", "item", "product");
        var makeCol = Col("manufacturer", "make");
        var modelCol = Col("model");
        var catCol = Col("category", "type");
        var qtyCol = Col("quantity", "qty", "opening", "remaining");
        var notesCol = Col("notes", "remarks");
        if (nameCol == 0 || qtyCol == 0)
            return [];

        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        var list = new List<ParsedStockGroup>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var r = 2; r <= last; r++)
        {
            var name = Clean(ws.Cell(r, nameCol).GetString());
            if (name is null) continue;
            var qty = 0;
            if (ws.Cell(r, qtyCol).TryGetValue(out double q))
                qty = (int)q;
            if (qty <= 0) continue;

            var manufacturer = makeCol == 0 ? null : Clean(ws.Cell(r, makeCol).GetString());
            var model = modelCol == 0 ? null : Clean(ws.Cell(r, modelCol).GetString());
            var category = catCol == 0 ? null : Clean(ws.Cell(r, catCol).GetString());
            var notes = notesCol == 0 ? null : Clean(ws.Cell(r, notesCol).GetString());
            var parsed = ParseProduct(name);
            var key = NormalizeKey(name);
            if (!seen.Add(key))
                continue;

            list.Add(new ParsedStockGroup
            {
                Key = key,
                Name = name,
                Manufacturer = manufacturer ?? parsed.Manufacturer,
                Model = model ?? parsed.Model,
                Category = category ?? parsed.Category,
                Classification = Classify(name, manufacturer, model),
                OpeningQty = qty,
                Notes = notes,
                Rows = []
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
        public int OpeningQty { get; set; }
        public string? Notes { get; set; }
        public List<UnitRow> Rows { get; set; } = [];
    }
}
