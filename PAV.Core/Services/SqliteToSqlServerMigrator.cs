using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;

namespace PAV.Core.Services;

public sealed class MigrationReport
{
    public bool Ok { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Errors { get; set; } = [];
    public List<MigrationTableCount> Tables { get; set; } = [];
}

public sealed class MigrationTableCount
{
    public string Table { get; set; } = "";
    public int Source { get; set; }
    public int Destination { get; set; }
    public bool Match => Source == Destination;
}

/// <summary>
/// Copies every PAV table from one provider to another, preserving Ids.
/// Used for SQLite → SQL Server (cutover) and SQL Server → SQLite (snapshot).
/// Does not delete the source database.
/// </summary>
public sealed class SqliteToSqlServerMigrator
{
    private static readonly HashSet<string> IdentityTables =
    [
        "Locations", "Categories", "Users", "Assets", "AssetHistory",
        "IpRanges", "IpRecords", "StockItems", "StockMovements"
    ];

    public async Task<MigrationReport> CopyAsync(PavDatabase source, PavDatabase destination, bool replaceDestination)
    {
        var report = new MigrationReport();
        await source.OpenAsync();
        await destination.OpenAsync();

        await using var src = source.Create();
        await using var dst = destination.Create();

        var locations = await src.Locations.AsNoTracking().ToListAsync();
        var categories = await src.Categories.AsNoTracking().ToListAsync();
        var users = await src.Users.AsNoTracking().ToListAsync();
        var assets = await src.Assets.AsNoTracking().ToListAsync();
        var history = await src.AssetHistory.AsNoTracking().ToListAsync();
        var ranges = await src.IpRanges.AsNoTracking().ToListAsync();
        var ips = await src.IpRecords.AsNoTracking().ToListAsync();
        var stock = await src.StockItems.AsNoTracking().ToListAsync();
        var moves = await src.StockMovements.AsNoTracking().ToListAsync();

        if (!replaceDestination &&
            (await dst.Users.AsNoTracking().AnyAsync() ||
             await dst.Assets.AsNoTracking().AnyAsync() ||
             await dst.StockItems.AsNoTracking().AnyAsync()))
        {
            report.Errors.Add("Destination already has PAV data. Migration refused so existing data is not overwritten. Re-run with replace only after a backup.");
            report.Summary = "Aborted — destination is not empty.";
            return report;
        }

        await using var tx = await dst.Database.BeginTransactionAsync();
        try
        {
            if (replaceDestination)
                await WipeDestinationAsync(dst);

            await InsertAsync(dst, "Locations", locations);
            await InsertAsync(dst, "Categories", categories);
            await InsertAsync(dst, "Users", users);
            await InsertAsync(dst, "Assets", assets);
            await InsertAsync(dst, "AssetHistory", history);
            await InsertAsync(dst, "IpRanges", ranges);
            await InsertAsync(dst, "IpRecords", ips);
            await InsertAsync(dst, "StockItems", stock);
            await InsertAsync(dst, "StockMovements", moves);

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            report.Errors.Add(ex.Message);
            report.Ok = false;
            report.Summary = "Migration failed. Source database was not changed.";
            return report;
        }

        report.Tables =
        [
            await CountAsync(src, dst, "Locations", db => db.Locations.CountAsync()),
            await CountAsync(src, dst, "Categories", db => db.Categories.CountAsync()),
            await CountAsync(src, dst, "Users", db => db.Users.CountAsync()),
            await CountAsync(src, dst, "Assets", db => db.Assets.CountAsync()),
            await CountAsync(src, dst, "AssetHistory", db => db.AssetHistory.CountAsync()),
            await CountAsync(src, dst, "IpRanges", db => db.IpRanges.CountAsync()),
            await CountAsync(src, dst, "IpRecords", db => db.IpRecords.CountAsync()),
            await CountAsync(src, dst, "StockItems", db => db.StockItems.CountAsync()),
            await CountAsync(src, dst, "StockMovements", db => db.StockMovements.CountAsync())
        ];

        var mismatch = report.Tables.Where(t => !t.Match).ToList();
        report.Ok = mismatch.Count == 0;
        report.Summary = report.Ok
            ? $"Copied {report.Tables.Sum(t => t.Source)} rows. Counts match."
            : "Copied, but some table counts do not match. Check the report before switching clients.";
        foreach (var t in mismatch)
            report.Errors.Add($"{t.Table}: source {t.Source}, destination {t.Destination}");

        await ValidateIntegrityAsync(dst, report);
        if (report.Errors.Count > 0)
            report.Ok = false;
        if (report.Ok)
            report.Summary = $"Copied {report.Tables.Sum(t => t.Source)} rows. Counts and relationships match.";
        return report;
    }

    private static async Task ValidateIntegrityAsync(AppDbContext dst, MigrationReport report)
    {
        var userIds = await dst.Users.AsNoTracking().Select(u => u.Id).ToListAsync();
        var assetIds = await dst.Assets.AsNoTracking().Select(a => a.Id).ToListAsync();
        if (userIds.Count != userIds.Distinct().Count()) report.Errors.Add("Users: duplicate primary keys.");
        if (assetIds.Count != assetIds.Distinct().Count()) report.Errors.Add("Assets: duplicate primary keys.");

        var orphanAssign = await dst.Assets.AsNoTracking()
            .CountAsync(a => a.AssignedUserId != null && !dst.Users.Any(u => u.Id == a.AssignedUserId));
        if (orphanAssign > 0)
            report.Errors.Add($"Assets: {orphanAssign} AssignedUserId values do not match a User.");

        var badCat = await dst.Assets.AsNoTracking()
            .CountAsync(a => !dst.Categories.Any(c => c.Id == a.CategoryId));
        if (badCat > 0)
            report.Errors.Add($"Assets: {badCat} rows have a missing Category.");

        var badLoc = await dst.Assets.AsNoTracking()
            .CountAsync(a => a.LocationId != null && !dst.Locations.Any(l => l.Id == a.LocationId));
        if (badLoc > 0)
            report.Errors.Add($"Assets: {badLoc} rows have a missing Location.");

        var orphanHist = await dst.AssetHistory.AsNoTracking()
            .CountAsync(h => !dst.Assets.Any(a => a.Id == h.AssetId));
        if (orphanHist > 0)
            report.Errors.Add($"AssetHistory: {orphanHist} rows point at a missing Asset.");

        var orphanMoveItem = await dst.StockMovements.AsNoTracking()
            .CountAsync(m => !dst.StockItems.Any(s => s.Id == m.StockItemId));
        if (orphanMoveItem > 0)
            report.Errors.Add($"StockMovements: {orphanMoveItem} rows point at a missing StockItem.");

        var orphanMoveUser = await dst.StockMovements.AsNoTracking()
            .CountAsync(m => m.UserId != null && !dst.Users.Any(u => u.Id == m.UserId));
        if (orphanMoveUser > 0)
            report.Errors.Add($"StockMovements: {orphanMoveUser} rows point at a missing User.");

        var orphanIp = await dst.IpRecords.AsNoTracking()
            .CountAsync(i => !dst.IpRanges.Any(r => r.Id == i.RangeId));
        if (orphanIp > 0)
            report.Errors.Add($"IpRecords: {orphanIp} rows point at a missing IpRange.");
    }

    private static async Task<MigrationTableCount> CountAsync(
        AppDbContext src, AppDbContext dst, string table, Func<AppDbContext, Task<int>> count)
    {
        return new MigrationTableCount
        {
            Table = table,
            Source = await count(src),
            Destination = await count(dst)
        };
    }

    private static async Task WipeDestinationAsync(AppDbContext dst)
    {
        await dst.StockMovements.ExecuteDeleteAsync();
        await dst.StockItems.ExecuteDeleteAsync();
        await dst.IpRecords.ExecuteDeleteAsync();
        await dst.IpRanges.ExecuteDeleteAsync();
        await dst.AssetHistory.ExecuteDeleteAsync();
        await dst.Assets.ExecuteDeleteAsync();
        await dst.Users.ExecuteDeleteAsync();
        await dst.Categories.ExecuteDeleteAsync();
        await dst.Locations.ExecuteDeleteAsync();
    }

    private static async Task InsertAsync<T>(AppDbContext dst, string table, List<T> rows) where T : class
    {
        if (rows.Count == 0) return;
        if (!IdentityTables.Contains(table))
            throw new InvalidOperationException("Unknown table: " + table);
        var sqlServer = dst.Database.IsSqlServer();
        if (sqlServer)
            await dst.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [" + table + "] ON");
        try
        {
            foreach (var chunk in rows.Chunk(200))
            {
                foreach (var row in chunk)
                {
                    dst.Set<T>().Add(row);
                    var id = dst.Entry(row).Property("Id");
                    id.CurrentValue = id.CurrentValue;
                    id.IsTemporary = false;
                }
                await dst.SaveChangesAsync();
                dst.ChangeTracker.Clear();
            }
        }
        finally
        {
            if (sqlServer)
                await dst.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [" + table + "] OFF");
        }
    }
}
