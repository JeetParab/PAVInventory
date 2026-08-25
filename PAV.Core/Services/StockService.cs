using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Core.Services;

public class StockService(AppDbContext db, SqliteWriteLock writeLock)
{
    public async Task<StockOverviewDto> OverviewAsync(string? search, string? status, bool includeInactive, string? categoryScope = null)
    {
        var items = await QueryItems(search, status, includeInactive, categoryScope).ToListAsync();
        var ids = items.Select(x => x.Id).ToList();
        var totals = await MovementTotalsAsync(ids);

        var dtos = items.Select(i => ToDto(i, totals.GetValueOrDefault(i.Id))).ToList();
        return new StockOverviewDto
        {
            ItemCount = dtos.Count,
            OnHandUnits = dtos.Where(x => x.IsActive).Sum(x => x.OnHand),
            LowStock = dtos.Count(x => x.IsLow && x.IsActive),
            OutOfStock = dtos.Count(x => x.OnHand <= 0 && x.IsActive),
            Items = dtos
        };
    }

    public async Task<StockItemDto> GetAsync(int id)
    {
        var item = await db.StockItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
                   ?? throw new AppException(404, "not_found", "Stock item not found.");
        var totals = await MovementTotalsAsync([id]);
        return ToDto(item, totals.GetValueOrDefault(id));
    }

    public async Task<List<StockMovementDto>> MovementsAsync(int? itemId, int? userId, string? search, string? categoryScope = null)
    {
        var q = db.StockMovements.AsNoTracking()
            .Include(x => x.StockItem)
            .Include(x => x.User)
            .AsQueryable();
        if (itemId is > 0) q = q.Where(x => x.StockItemId == itemId);
        if (userId is > 0) q = q.Where(x => x.UserId == userId);
        q = ApplyCategoryScope(q, categoryScope);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x =>
                x.StockItem.Name.ToLower().Contains(s) ||
                (x.AssignedUserName != null && x.AssignedUserName.ToLower().Contains(s)) ||
                (x.User != null && x.User.Name.ToLower().Contains(s)) ||
                (x.Reference != null && x.Reference.ToLower().Contains(s)) ||
                (x.Notes != null && x.Notes.ToLower().Contains(s)) ||
                (x.SerialNumber != null && x.SerialNumber.ToLower().Contains(s)));
        }

        var rows = await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(2000).ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public async Task<StockItemDto> SaveItemAsync(int? id, SaveStockItemRequest req, CurrentUser actor)
    {
        RequireAdmin(actor);
        var name = Mapping.Clean(req.Name) ?? throw new AppException(400, "validation", "Name is required.");
        var manufacturer = Mapping.Clean(req.Manufacturer);
        var model = Mapping.Clean(req.Model);
        var key = StockExcel.IdentityKey(name, manufacturer, model);


        return await writeLock.WriteAsync(async () =>
        {
            StockItem item;
            if (id is > 0)
            {
                item = await db.StockItems.FirstOrDefaultAsync(x => x.Id == id)
                       ?? throw new AppException(404, "not_found", "Stock item not found.");
            }
            else
            {
                item = new StockItem { CreatedAt = DateTime.Now };
                db.StockItems.Add(item);
            }

            var clash = await db.StockItems.AsNoTracking()
                .FirstOrDefaultAsync(x => x.NormalizedKey == key && x.Id != (id ?? 0));
            if (clash is not null)
                throw new AppException(409, "conflict",
                    $"A stock item with the same name/make/model already exists ('{clash.Name}').");


            item.Name = name;
            item.NormalizedKey = key;
            item.Category = Mapping.Clean(req.Category) ?? StockExcel.GuessCategory(name);
            item.Manufacturer = manufacturer;
            item.Model = model;
            item.Unit = Mapping.Clean(req.Unit) ?? "pcs";
            item.MinimumQuantity = Math.Max(0, req.MinimumQuantity);
            item.IsActive = req.IsActive;
            item.Notes = Mapping.Clean(req.Notes);
            item.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
            return await GetAsync(item.Id);
        });
    }

    public Task<StockItemDto> ReceiveAsync(StockMoveRequest req, CurrentUser actor) =>
        MoveAsync(StockMovementType.Receive, req, actor);

    public Task<StockItemDto> IssueAsync(StockMoveRequest req, CurrentUser actor) =>
        MoveAsync(StockMovementType.Issue, req, actor);

    public Task<StockItemDto> ReturnAsync(StockMoveRequest req, CurrentUser actor) =>
        MoveAsync(StockMovementType.Return, req, actor);

    public Task<StockItemDto> AdjustAsync(StockMoveRequest req, CurrentUser actor)
    {
        RequireAdmin(actor);
        return MoveAsync(StockMovementType.Adjustment, req, actor);
    }

    private async Task<StockItemDto> MoveAsync(StockMovementType type, StockMoveRequest req, CurrentUser actor)
    {
        if (type != StockMovementType.Adjustment)
            RequireAssign(actor);

        var qty = req.Quantity;
        if (type == StockMovementType.Adjustment)
        {
            if (qty == 0)
                throw new AppException(400, "validation", "Adjustment cannot be zero.");
        }
        else if (qty <= 0)
        {
            throw new AppException(400, "validation", "Quantity must be greater than zero.");
        }

        if (type is StockMovementType.Issue or StockMovementType.Return)
        {
            var who = Mapping.Clean(req.AssignedUserName);
            if (req.UserId is null && who is null)
                throw new AppException(400, "validation", "Pick a user (or type the name).");
        }

        return await writeLock.WriteAsync(async () =>
        {
            var item = await db.StockItems.FirstOrDefaultAsync(x => x.Id == req.StockItemId)
                       ?? throw new AppException(404, "not_found", "Stock item not found.");
            if (!item.IsActive && type != StockMovementType.Adjustment)
                throw new AppException(400, "validation", $"{item.Name} is inactive.");

            var users = await db.Users.AsNoTracking()
                .Select(u => new { u.Id, u.Name, u.Username })
                .ToListAsync();
            var userTuples = users.Select(u => (u.Id, u.Name, u.Username)).ToList();
            var resolved = UserNameResolver.ResolveMovement(req.UserId, req.AssignedUserName, userTuples);
            var userId = resolved.UserId;
            var userName = resolved.UserName;

            var signed = type.SignedDelta(qty);
            var next = item.OnHand + signed;
            if (next < 0)
                throw new AppException(400, "validation",
                    $"Insufficient stock. {item.Name} on hand is {item.OnHand}.");

            if (type is StockMovementType.Issue or StockMovementType.Return or StockMovementType.Adjustment)
            {
                if (type == StockMovementType.Adjustment && string.IsNullOrWhiteSpace(req.Reference) && string.IsNullOrWhiteSpace(req.Notes))
                    throw new AppException(400, "validation", "Give a reason for the adjustment.");
            }

            var now = DateTime.Now;
            db.StockMovements.Add(new StockMovement
            {
                StockItemId = item.Id,
                MovementType = type,
                Quantity = type == StockMovementType.Adjustment ? qty : Math.Abs(qty),
                UserId = userId,
                AssignedUserName = userName,
                Reference = Mapping.Clean(req.Reference),
                Notes = Mapping.Clean(req.Notes),
                SerialNumber = Mapping.Clean(req.SerialNumber),
                CreatedBy = actor.Username,
                CreatedAt = now
            });
            item.OnHand = next;
            item.UpdatedAt = now;
            await db.SaveChangesAsync();
            return await GetAsync(item.Id);
        });
    }

    public async Task<StockImportPreviewDto> PreviewImportAsync(Stream stream, string fileName)
    {
        var existing = await LoadExistingStockAsync();
        var users = await LoadUserTuplesAsync();
        return StockExcel.BuildPlan(stream, fileName, existing, users).Preview;
    }

    public async Task<StockImportResultDto> ImportAsync(Stream stream, string fileName, CurrentUser actor)
    {
        RequireAdmin(actor);
        stream.Position = 0;
        var existing = await LoadExistingStockAsync();
        var users = await LoadUserTuplesAsync();
        var plan = StockExcel.BuildPlan(stream, fileName, existing, users);
        if (!plan.Preview.CanImport)
            throw new AppException(400, "validation", plan.Preview.Summary, plan.Preview.Issues);

        return await writeLock.WriteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var created = 0;
                var opening = 0;
                var issues = 0;
                var skipped = 0;
                var now = DateTime.Now;

                foreach (var g in plan.Groups)
                {
                    if (g.Action != "Create")
                    {
                        skipped++;
                        continue;
                    }

                    var item = new StockItem
                    {
                        Name = g.Name,
                        NormalizedKey = g.Key,
                        Category = g.Category,
                        Manufacturer = g.Manufacturer,
                        Model = g.Model,
                        Unit = "pcs",
                        MinimumQuantity = 0,
                        IsActive = true,
                        NeedsReview = g.Classification == "Ambiguous",
                        Notes = Mapping.Clean(g.Notes) ?? "Imported from " + fileName,
                        OnHand = 0,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.StockItems.Add(item);
                    await db.SaveChangesAsync();

                    var openQty = g.OpeningQty;
                    db.StockMovements.Add(new StockMovement
                    {
                        StockItemId = item.Id,
                        MovementType = StockMovementType.OpeningBalance,
                        Quantity = openQty,
                        Reference = Path.GetFileName(fileName),
                        Notes = Mapping.Clean(g.Notes) ?? $"{openQty} units opening balance from {Path.GetFileName(fileName)}.",
                        CreatedBy = actor.Username,
                        CreatedAt = now
                    });
                    item.OnHand += openQty;
                    opening += openQty;
                    created++;

                    foreach (var row in g.Rows.Where(x => x.IsIssued))
                    {
                        var userName = Mapping.Clean(row.User);
                        var userId = UserNameResolver.ResolveUniqueId(users, userName);
                        var when = row.Date ?? now;
                        db.StockMovements.Add(new StockMovement
                        {
                            StockItemId = item.Id,
                            MovementType = StockMovementType.Issue,
                            Quantity = 1,
                            UserId = userId,
                            AssignedUserName = userName,
                            SerialNumber = Mapping.Clean(row.Serial),
                            Reference = row.Sr > 0 ? $"Sr {row.Sr}" : null,
                            Notes = JoinNotes(row.Status, row.Remarks, "Imported issued row."),
                            CreatedBy = actor.Username,
                            CreatedAt = when
                        });
                        item.OnHand -= 1;
                        issues++;
                    }

                    if (item.OnHand < 0)
                        throw new AppException(400, "validation",
                            $"{item.Name}: issued count exceeds opening. File was not imported.");

                    item.UpdatedAt = now;
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return new StockImportResultDto
                {
                    ItemsCreated = created,
                    OpeningUnits = opening,
                    IssuesRecorded = issues,
                    SkippedExisting = skipped,
                    Summary = $"Created {created} items. Opening {opening} units. Recorded {issues} already-issued. Skipped {skipped}."
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }

    public async Task<StockIntegrityDto> IntegrityCheckAsync(CurrentUser actor)
    {
        RequireAdmin(actor);
        var items = await db.StockItems.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var moves = await db.StockMovements.AsNoTracking()
            .Select(m => new { m.StockItemId, m.MovementType, m.Quantity })
            .ToListAsync();
        var calc = moves
            .GroupBy(m => m.StockItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.MovementType.SignedDelta(x.Quantity)));

        var rows = items.Select(i =>
        {
            var calculated = calc.GetValueOrDefault(i.Id);
            return new StockIntegrityRowDto
            {
                Id = i.Id,
                Name = i.Name,
                StoredOnHand = i.OnHand,
                CalculatedOnHand = calculated,
                Status = i.OnHand == calculated ? "PASS" : "MISMATCH"
            };
        }).ToList();

        var pass = rows.Count(r => r.Status == "PASS");
        var bad = rows.Count - pass;
        return new StockIntegrityDto
        {
            ItemCount = rows.Count,
            PassCount = pass,
            MismatchCount = bad,
            Summary = bad == 0
                ? $"PASS — {pass} items, stored OnHand matches movement ledger."
                : $"MISMATCH — {bad} of {rows.Count} items differ from the movement ledger. Nothing was changed.",
            Rows = rows
        };
    }

    private async Task<List<StockExcel.ExistingStock>> LoadExistingStockAsync()
    {
        var rows = await db.StockItems.AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.NormalizedKey, x.Manufacturer, x.Model })
            .ToListAsync();
        return rows.Select(x => new StockExcel.ExistingStock(x.Id, x.Name, x.NormalizedKey, x.Manufacturer, x.Model)).ToList();
    }

    private async Task<List<(int Id, string Name, string Username)>> LoadUserTuplesAsync()
    {
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.Name, u.Username }).ToListAsync();
        return users.Select(u => (u.Id, u.Name, u.Username)).ToList();
    }

    public async Task<byte[]> ExportAsync()
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var items = await db.StockItems.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var ws = wb.AddWorksheet("Stock");
        var headers = new[] { "Name", "Manufacturer", "Model", "Category", "Unit", "OnHand", "Minimum", "Status", "Active", "NeedsReview", "Notes" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        var r = 2;
        foreach (var i in items)
        {
            ws.Cell(r, 1).Value = i.Name;
            ws.Cell(r, 2).Value = i.Manufacturer;
            ws.Cell(r, 3).Value = i.Model;
            ws.Cell(r, 4).Value = i.Category;
            ws.Cell(r, 5).Value = i.Unit;
            ws.Cell(r, 6).Value = i.OnHand;
            ws.Cell(r, 7).Value = i.MinimumQuantity;
            ws.Cell(r, 8).Value = StatusOf(i);
            ws.Cell(r, 9).Value = i.IsActive ? "Yes" : "No";
            ws.Cell(r, 10).Value = i.NeedsReview ? "Yes" : "No";
            ws.Cell(r, 11).Value = i.Notes;
            r++;
        }
        ws.SheetView.FreezeRows(1);

        var moves = await db.StockMovements.AsNoTracking().Include(x => x.StockItem)
            .OrderByDescending(x => x.CreatedAt).ToListAsync();
        var wm = wb.AddWorksheet("Movements");
        var mh = new[] { "When", "Item", "Type", "Qty", "Signed", "User", "Serial", "Reference", "Notes", "By" };
        for (var i = 0; i < mh.Length; i++)
            wm.Cell(1, i + 1).Value = mh[i];
        r = 2;
        foreach (var m in moves)
        {
            wm.Cell(r, 1).Value = m.CreatedAt;
            wm.Cell(r, 2).Value = m.StockItem.Name;
            wm.Cell(r, 3).Value = m.MovementType.Display();
            wm.Cell(r, 4).Value = m.Quantity;
            wm.Cell(r, 5).Value = m.MovementType.SignedDelta(m.Quantity);
            wm.Cell(r, 6).Value = m.AssignedUserName;
            wm.Cell(r, 7).Value = m.SerialNumber;
            wm.Cell(r, 8).Value = m.Reference;
            wm.Cell(r, 9).Value = m.Notes;
            wm.Cell(r, 10).Value = m.CreatedBy;
            r++;
        }
        wm.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private IQueryable<StockItem> QueryItems(string? search, string? status, bool includeInactive, string? categoryScope = null)
    {
        var q = db.StockItems.AsNoTracking().AsQueryable();
        if (!includeInactive)
            q = q.Where(x => x.IsActive);
        q = ApplyCategoryScope(q, categoryScope);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x =>
                x.Name.ToLower().Contains(s) ||
                (x.Manufacturer != null && x.Manufacturer.ToLower().Contains(s)) ||
                (x.Model != null && x.Model.ToLower().Contains(s)) ||
                (x.Category != null && x.Category.ToLower().Contains(s)) ||
                (x.Notes != null && x.Notes.ToLower().Contains(s)));
        }

        var filter = (status ?? "all").Trim().ToLowerInvariant();
        q = filter switch
        {
            "low" or "low stock" => q.Where(x => x.MinimumQuantity > 0 && x.OnHand <= x.MinimumQuantity && x.OnHand > 0),
            "out" or "out of stock" => q.Where(x => x.OnHand <= 0),
            "in" or "in stock" => q.Where(x => x.OnHand > 0 && (x.MinimumQuantity <= 0 || x.OnHand > x.MinimumQuantity)),
            "review" => q.Where(x => x.NeedsReview),
            "inactive" => q.Where(x => !x.IsActive),
            _ => q
        };
        return q.OrderBy(x => x.Name);
    }

    private static IQueryable<StockItem> ApplyCategoryScope(IQueryable<StockItem> q, string? categoryScope)
    {
        var scope = (categoryScope ?? "all").Trim().ToLowerInvariant();
        if (scope == "toner")
            return q.Where(x =>
                (x.Category != null && x.Category.ToLower() == "toner")
                || x.Name.ToLower().Contains("toner")
                || x.Name.ToLower().Contains("cartridge"));
        if (scope == "stock")
            return q.Where(x =>
                (x.Category == null || x.Category.ToLower() != "toner")
                && !x.Name.ToLower().Contains("toner")
                && !x.Name.ToLower().Contains("cartridge"));
        return q;
    }

    private static IQueryable<StockMovement> ApplyCategoryScope(IQueryable<StockMovement> q, string? categoryScope)
    {
        var scope = (categoryScope ?? "all").Trim().ToLowerInvariant();
        if (scope == "toner")
            return q.Where(x =>
                (x.StockItem.Category != null && x.StockItem.Category.ToLower() == "toner")
                || x.StockItem.Name.ToLower().Contains("toner")
                || x.StockItem.Name.ToLower().Contains("cartridge"));
        if (scope == "stock")
            return q.Where(x =>
                (x.StockItem.Category == null || x.StockItem.Category.ToLower() != "toner")
                && !x.StockItem.Name.ToLower().Contains("toner")
                && !x.StockItem.Name.ToLower().Contains("cartridge"));
        return q;
    }

    private async Task<Dictionary<int, (int Open, int Rec, int Iss, int Ret, int Adj)>> MovementTotalsAsync(List<int> ids)
    {
        if (ids.Count == 0) return [];
        var rows = await db.StockMovements.AsNoTracking()
            .Where(x => ids.Contains(x.StockItemId))
            .GroupBy(x => new { x.StockItemId, x.MovementType })
            .Select(g => new { g.Key.StockItemId, g.Key.MovementType, Qty = g.Sum(x => x.Quantity) })
            .ToListAsync();
        var map = new Dictionary<int, (int Open, int Rec, int Iss, int Ret, int Adj)>();
        foreach (var id in ids)
            map[id] = (0, 0, 0, 0, 0);
        foreach (var row in rows)
        {
            var cur = map.GetValueOrDefault(row.StockItemId);
            map[row.StockItemId] = row.MovementType switch
            {
                StockMovementType.OpeningBalance => (cur.Open + row.Qty, cur.Rec, cur.Iss, cur.Ret, cur.Adj),
                StockMovementType.Receive => (cur.Open, cur.Rec + row.Qty, cur.Iss, cur.Ret, cur.Adj),
                StockMovementType.Issue => (cur.Open, cur.Rec, cur.Iss + row.Qty, cur.Ret, cur.Adj),
                StockMovementType.Return => (cur.Open, cur.Rec, cur.Iss, cur.Ret + row.Qty, cur.Adj),
                StockMovementType.Adjustment => (cur.Open, cur.Rec, cur.Iss, cur.Ret, cur.Adj + row.Qty),
                _ => cur
            };
        }
        return map;
    }

    private static StockItemDto ToDto(StockItem i, (int Open, int Rec, int Iss, int Ret, int Adj) t)
    {
        var status = StatusOf(i);
        return new StockItemDto
        {
            Id = i.Id,
            Name = i.Name,
            Category = i.Category,
            Manufacturer = i.Manufacturer,
            Model = i.Model,
            Unit = i.Unit,
            OnHand = i.OnHand,
            MinimumQuantity = i.MinimumQuantity,
            IsActive = i.IsActive,
            NeedsReview = i.NeedsReview,
            Notes = i.Notes,
            StockStatus = status,
            IsLow = status == "Low Stock",
            Opening = t.Open,
            Received = t.Rec,
            Issued = t.Iss,
            Returned = t.Ret,
            Adjusted = t.Adj,
            UpdatedAt = i.UpdatedAt
        };
    }

    private static string StatusOf(StockItem i)
    {
        if (!i.IsActive) return "Inactive";
        if (i.OnHand <= 0) return "Out of Stock";
        if (i.MinimumQuantity > 0 && i.OnHand <= i.MinimumQuantity) return "Low Stock";
        return "In Stock";
    }

    private static StockMovementDto ToDto(StockMovement m) => new()
    {
        Id = m.Id,
        StockItemId = m.StockItemId,
        ItemName = m.StockItem?.Name ?? "",
        MovementType = m.MovementType.Display(),
        Quantity = m.Quantity,
        SignedQuantity = m.MovementType.SignedDelta(m.Quantity),
        UserId = m.UserId,
        AssignedUser = m.User?.Name ?? m.AssignedUserName,
        Reference = m.Reference,
        Notes = m.Notes,
        SerialNumber = m.SerialNumber,
        CreatedBy = m.CreatedBy,
        CreatedAt = m.CreatedAt
    };

    private static string? JoinNotes(params string?[] parts)
    {
        var list = parts.Select(Mapping.Clean).Where(s => s is not null).Cast<string>().ToList();
        return list.Count == 0 ? null : string.Join(" · ", list);
    }

    private static void RequireAssign(CurrentUser actor)
    {
        if (!actor.Can(Permissions.Assign))
            throw new AppException(403, "forbidden", "You do not have permission to do that.");
    }

    private static void RequireAdmin(CurrentUser actor)
    {
        if (actor.Role != UserRole.Administrator)
            throw new AppException(403, "forbidden", "Only an administrator can do that.");
    }
}
