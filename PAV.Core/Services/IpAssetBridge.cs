using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Core.Services;

/// <summary>
/// Keeps Floor 1–7 IP pool rows in step with serialized assets.
/// Match is by IP address string. Wi-Fi / other subnets are ignored.
/// </summary>
public static class IpAssetBridge
{
    public static async Task<string?> AfterIpAssignedAsync(
        AppDbContext db, IpRecord rec, bool addToInventory, CurrentUser actor)
    {
        if (!addToInventory)
            return null;

        var ip = rec.Address;
        var users = await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.Name, u.Username })
            .ToListAsync();
        var tuples = users.Select(u => (u.Id, u.Name, u.Username)).ToList();
        var userId = UserNameResolver.ResolveUniqueId(tuples, rec.AssignedUser);
        var userName = Mapping.Clean(rec.AssignedUser);

        var matches = await db.Assets
            .Where(a => a.IpAddress != null && a.IpAddress == ip)
            .ToListAsync();

        if (matches.Count > 0)
        {
            foreach (var asset in matches)
                ApplyIpFields(asset, rec, userId, userName);
            await SqliteGuard.SaveChangesAsync(db);
            return matches.Count == 1
                ? $"Inventory updated: {matches[0].AssetTag} now has {ip}."
                : $"Inventory updated: {matches.Count} assets with {ip}.";
        }

        var categoryId = await CategoryForDeviceAsync(db, rec.DeviceType);
        var tag = await UniqueTagAsync(db, rec);
        var now = DateTime.UtcNow;
        var assetNew = new Asset
        {
            AssetTag = tag,
            CategoryId = categoryId,
            Hostname = Mapping.Clean(rec.AssignedDevice),
            IpAddress = ip,
            MacAddress = Mapping.Clean(rec.MacAddress),
            AssignedUserId = userId,
            AssignedUserName = userName,
            AssignedDate = userName is null ? null : now,
            Status = userName is null ? AssetStatus.InStock : AssetStatus.InUse,
            Remarks = "Created from IP Inventory",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Assets.Add(assetNew);
        await SqliteGuard.SaveChangesAsync(db);
        db.AssetHistory.Add(new AssetHistory
        {
            AssetId = assetNew.Id,
            Username = actor.Username,
            Action = HistoryAction.Created,
            FieldName = "IP Address",
            NewValue = ip,
            Timestamp = now
        });
        if (userName is not null)
        {
            db.AssetHistory.Add(new AssetHistory
            {
                AssetId = assetNew.Id,
                Username = actor.Username,
                Action = HistoryAction.Assigned,
                FieldName = "Assigned User",
                OldValue = "Unassigned",
                NewValue = userName,
                Timestamp = now
            });
        }
        await SqliteGuard.SaveChangesAsync(db);
        return $"Inventory asset {tag} created for {ip}.";
    }

    public static async Task AfterIpReleasedAsync(AppDbContext db, string address, CurrentUser actor)
    {
        if (!IpAddressService.TryNormalize(address, out var ip, out _, out _))
            return;
        var assets = await db.Assets.Where(a => a.IpAddress != null && a.IpAddress == ip).ToListAsync();
        if (assets.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var asset in assets)
        {
            db.AssetHistory.Add(new AssetHistory
            {
                AssetId = asset.Id,
                Username = actor.Username,
                Action = HistoryAction.Updated,
                FieldName = "IP Address",
                OldValue = asset.IpAddress,
                NewValue = "",
                Timestamp = now
            });
            asset.IpAddress = null;
            asset.Version++;
            asset.UpdatedAt = now;
        }
        await SqliteGuard.SaveChangesAsync(db);
    }

    public static async Task AfterAssetSavedAsync(AppDbContext db, Asset asset, string? previousIp, CurrentUser actor)
    {
        var oldIp = NormalizeOrNull(previousIp);
        var newIp = NormalizeOrNull(asset.IpAddress);
        if (string.Equals(oldIp, newIp, StringComparison.OrdinalIgnoreCase))
        {
            if (newIp is not null)
                await ClaimAsync(db, newIp, asset, actor);
            return;
        }
        if (oldIp is not null)
            await ReleaseIfUnusedAsync(db, oldIp, asset.Id, actor);
        if (newIp is not null)
            await ClaimAsync(db, newIp, asset, actor);
    }

    public static Task AfterAssetDeletedAsync(AppDbContext db, string? ip, int assetId, CurrentUser actor) =>
        ReleaseIfUnusedAsync(db, NormalizeOrNull(ip), assetId, actor);

    private static async Task ClaimAsync(AppDbContext db, string ip, Asset asset, CurrentUser actor)
    {
        var rec = await db.IpRecords.FirstOrDefaultAsync(x => x.Address == ip);
        if (rec is null) return;
        rec.Status = IpStatus.Used;
        rec.AssignedDevice = Mapping.Clean(asset.Hostname) ?? rec.AssignedDevice;
        rec.AssignedUser = Mapping.Clean(asset.AssignedUserName) ?? rec.AssignedUser;
        rec.MacAddress = Mapping.Clean(asset.MacAddress) ?? rec.MacAddress;
        rec.DateAssigned ??= DateTime.UtcNow;
        rec.LastUpdated = DateTime.UtcNow;
        rec.AllocatedBy = actor.DisplayName;
        await SqliteGuard.SaveChangesAsync(db);
    }

    private static async Task ReleaseIfUnusedAsync(AppDbContext db, string? ip, int exceptAssetId, CurrentUser actor)
    {
        if (ip is null) return;
        var rec = await db.IpRecords.FirstOrDefaultAsync(x => x.Address == ip);
        if (rec is null) return;
        var still = await db.Assets.AnyAsync(a => a.Id != exceptAssetId && a.IpAddress != null && a.IpAddress == ip);
        if (still) return;
        rec.Status = IpStatus.Free;
        rec.AssignedDevice = null;
        rec.AssignedUser = null;
        rec.Department = null;
        rec.MacAddress = null;
        rec.DeviceType = null;
        rec.DateAssigned = null;
        rec.LastUpdated = DateTime.UtcNow;
        rec.AllocatedBy = actor.DisplayName;
        await SqliteGuard.SaveChangesAsync(db);
    }

    private static void ApplyIpFields(Asset asset, IpRecord rec, int? userId, string? userName)
    {
        if (Mapping.Clean(rec.AssignedDevice) is { } host)
            asset.Hostname = host;
        if (Mapping.Clean(rec.MacAddress) is { } mac)
            asset.MacAddress = mac;
        if (userName is not null)
        {
            asset.AssignedUserId = userId;
            asset.AssignedUserName = userName;
            asset.AssignedDate ??= DateTime.UtcNow;
            if (asset.Status is AssetStatus.InStock or AssetStatus.Standby)
                asset.Status = AssetStatus.InUse;
        }
        asset.IpAddress = rec.Address;
        asset.UpdatedAt = DateTime.UtcNow;
        asset.Version++;
    }

    private static async Task<int> CategoryForDeviceAsync(AppDbContext db, string? deviceType)
    {
        var want = deviceType?.Trim().ToLowerInvariant() switch
        {
            "laptop" or "notebook" => "Laptop",
            "desktop" or "pc" => "Desktop",
            "all-in-one" or "aio" => "All-in-One",
            "printer" or "mfd" => "Printer",
            "monitor" or "display" => "Monitor",
            "ups" => "UPS",
            "access point" or "router" or "switch" or "firewall" or "network" => "Network",
            "voip phone" => "Accessory",
            _ => "Other"
        };
        var id = await db.Categories.AsNoTracking()
            .Where(c => c.Name.ToLower() == want.ToLower())
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();
        if (id is > 0) return id.Value;
        return await db.Categories.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
    }

    private static async Task<string> UniqueTagAsync(AppDbContext db, IpRecord rec)
    {
        var host = rec.HostOctet.ToString();
        var third = rec.Address.Split('.') is { Length: 4 } p ? p[2] : "0";
        var baseTag = Mapping.Clean(rec.AssignedDevice);
        if (baseTag is null || await db.Assets.AnyAsync(a => a.AssetTag.ToLower() == baseTag.ToLower()))
            baseTag = $"IP-{third}-{host}";
        var tag = baseTag;
        var n = 2;
        while (await db.Assets.AnyAsync(a => a.AssetTag.ToLower() == tag.ToLower()))
        {
            tag = baseTag + "-" + n;
            n++;
        }
        return tag;
    }

    private static string? NormalizeOrNull(string? raw) =>
        IpAddressService.TryNormalize(raw, out var ip, out _, out _) ? ip : Mapping.Clean(raw);
}
