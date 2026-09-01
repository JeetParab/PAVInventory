using Microsoft.EntityFrameworkCore;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;
using PAV.Core.Data;

namespace PAV.Core.Services;

public class AssetQuery
{
    public string? Search { get; set; }
    public AssetStatus? Status { get; set; }
    public int? CategoryId { get; set; }
    public int? LocationId { get; set; }
    public string? Manufacturer { get; set; }
    public bool? Assigned { get; set; }
    public int? AssignedUserId { get; set; }
    public string? SortBy { get; set; }

    public string? SortDir { get; set; }
}

public class AppException(int statusCode, string code, string message, List<string>? errors = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public List<string>? Errors { get; } = errors;
}

public class AssetService(AppDbContext db, IWriteLock writeLock)

{
    public async Task<List<AssetListDto>> ListAsync(AssetQuery q)
    {
        using var _ = Perf.Measure("ListAsync");
        var query = FilterAssets(db.Assets.AsNoTracking(), q);

        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var sort = q.SortBy?.ToLowerInvariant();
        if (sort is not "assigneduser")
        {
            query = sort switch
            {
                "assettag" or "assetid" => desc ? query.OrderByDescending(a => a.AssetTag) : query.OrderBy(a => a.AssetTag),
                "category" => desc ? query.OrderByDescending(a => a.Category.Name) : query.OrderBy(a => a.Category.Name),
                "manufacturer" => desc ? query.OrderByDescending(a => a.Manufacturer) : query.OrderBy(a => a.Manufacturer),
                "model" => desc ? query.OrderByDescending(a => a.Model) : query.OrderBy(a => a.Model),
                "serialnumber" => desc ? query.OrderByDescending(a => a.SerialNumber) : query.OrderBy(a => a.SerialNumber),
                "hostname" => desc ? query.OrderByDescending(a => a.Hostname) : query.OrderBy(a => a.Hostname),
                "location" => desc ? query.OrderByDescending(a => a.Location!.Name) : query.OrderBy(a => a.Location!.Name),
                "status" => desc ? query.OrderByDescending(a => a.Status) : query.OrderBy(a => a.Status),
                _ => query.OrderBy(a => a.AssetTag)
            };
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = await ProjectList(query).ToListAsync();
        Perf.Log("ListAsync.SQL", sw.ElapsedMilliseconds);

        foreach (var d in rows)
            d.Status = d.StatusValue.Display();

        if (sort is "assigneduser")
        {
            rows = desc
                ? rows.OrderByDescending(d => d.AssignedUser).ToList()
                : rows.OrderBy(d => d.AssignedUser).ToList();
        }

        return rows;
    }

    public async Task<InventoryLoadDto> InventoryAsync(AssetQuery q)
    {
        using var _ = Perf.Measure("InventoryAsync");
        var assets = await ListAsync(q);
        var look = new LookupService(db, writeLock);
        var cats = await look.CategoriesAsync();
        var locs = await look.LocationsAsync();
        var users = await look.UsersAsync();
        return new InventoryLoadDto
        {
            Assets = assets,
            Categories = cats,
            Locations = locs,
            Users = users
        };
    }

    private static IQueryable<Asset> FilterAssets(IQueryable<Asset> query, AssetQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(a =>
                a.AssetTag.ToLower().Contains(s) ||
                (a.SerialNumber != null && a.SerialNumber.ToLower().Contains(s)) ||
                (a.Hostname != null && a.Hostname.ToLower().Contains(s)) ||
                (a.IpAddress != null && a.IpAddress.ToLower().Contains(s)) ||
                (a.Manufacturer != null && a.Manufacturer.ToLower().Contains(s)) ||
                (a.Model != null && a.Model.ToLower().Contains(s)) ||
                (a.AssignedUserName != null && a.AssignedUserName.ToLower().Contains(s)) ||
                (a.AssignedUser != null && a.AssignedUser.Name.ToLower().Contains(s)) ||
                (a.MacAddress != null && a.MacAddress.ToLower().Contains(s)) ||
                (a.Location != null && a.Location.Name.ToLower().Contains(s)) ||
                (a.Category != null && a.Category.Name.ToLower().Contains(s)) ||
                (a.Designation != null && a.Designation.ToLower().Contains(s)));
        }

        if (q.Status is { } status)
            query = query.Where(a => a.Status == status);
        if (q.CategoryId is { } cat)
            query = query.Where(a => a.CategoryId == cat);
        if (q.LocationId is { } loc)
            query = query.Where(a => a.LocationId == loc);
        if (!string.IsNullOrWhiteSpace(q.Manufacturer))
            query = query.Where(a => a.Manufacturer == q.Manufacturer);
        if (q.Assigned is true)
            query = query.Where(a => a.AssignedUserId != null
                || (a.AssignedUserName != null && a.AssignedUserName != ""));
        else if (q.Assigned is false)
            query = query.Where(a => a.AssignedUserId == null
                && (a.AssignedUserName == null || a.AssignedUserName == ""));
        if (q.AssignedUserId is > 0)
            query = query.Where(a => a.AssignedUserId == q.AssignedUserId);
        return query;

    }

    private static IQueryable<AssetListDto> ProjectList(IQueryable<Asset> query) =>
        query.Select(a => new AssetListDto
        {
            Id = a.Id,
            AssetTag = a.AssetTag,
            SrNo = a.SrNo,
            CategoryId = a.CategoryId,
            Category = a.Category.Name,
            Manufacturer = a.Manufacturer,
            Model = a.Model,
            SerialNumber = a.SerialNumber,
            Hostname = a.Hostname,
            IpAddress = a.IpAddress,
            LocationId = a.LocationId,
            Location = a.Location != null ? a.Location.Name : null,
            AssignedUserId = a.AssignedUserId,
            AssignedUser = a.AssignedUser != null ? a.AssignedUser.Name : a.AssignedUserName,
            Designation = a.Designation,
            AlternateUser = a.AlternateUser,
            StatusValue = a.Status,
            CategoryFamily = a.Category.Family,
            Domain = a.Domain,
            MacAddress = a.MacAddress,
            Processor = a.Processor,
            Ram = a.Ram,
            Storage = a.Storage,
            OperatingSystem = a.OperatingSystem,
            DcInstalled = a.DcInstalled,
            AvInstalled = a.AvInstalled,
            MsOfficeVersion = a.MsOfficeVersion,
            MfaEnabled = a.MfaEnabled,
            IvantiInstalled = a.IvantiInstalled,
            AdminRights = a.AdminRights,
            UsbAccess = a.UsbAccess,
            ChromeUpdated = a.ChromeUpdated,
            StockAvailability = a.StockAvailability,
            StockWorking = a.StockWorking,
            PmCompleted = a.PmCompleted,
            LastConnected = a.LastConnected,
            CollectBy = a.CollectBy,
            Remarks = a.Remarks,
            PurchaseDate = a.PurchaseDate,
            WarrantyExpiry = a.WarrantyExpiry,
            Version = a.Version,
            IsTemporary = a.IsTemporary,
            NeedsReview = a.NeedsReview,
            MeLogon = a.MeLogon
        });

    public async Task<AssetDetailDto> GetAsync(int id, bool history = true)
    {
        using var _ = Perf.Measure(history ? "GetAsync" : "GetAsync.noHistory");
        var asset = await db.Assets
            .AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.Location)
            .Include(a => a.AssignedUser)
            .FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new AppException(404, "not_found", "Asset not found.");

        var hist = history ? await LoadHistoryAsync(id) : [];
        return Mapping.ToDetailDto(asset, hist);
    }

    public async Task<List<HistoryDto>> GetHistoryAsync(int id)
    {
        if (!await db.Assets.AsNoTracking().AnyAsync(a => a.Id == id) &&
            !await db.AssetHistory.AsNoTracking().AnyAsync(h => h.AssetId == id))
            throw new AppException(404, "not_found", "Asset not found.");
        return await LoadHistoryAsync(id);
    }

    public Task<AssetDetailDto> CreateAsync(SaveAssetRequest req, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var errors = await ValidateAsync(req, excludeId: null);
            if (errors.Count > 0)
                throw new AppException(400, "validation", "Please correct the highlighted fields.", errors);

            var now = DateTime.UtcNow;
            var users = await LoadUserKeysAsync();
            var asset = new Asset
            {
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            Mapping.Apply(asset, req);
            UserNameResolver.ApplyTo(asset, req.AssignedUserId, req.AssignedUserName, users);
            asset.AssignedDate = string.IsNullOrWhiteSpace(asset.AssignedUserName) ? null : now;

            await using var tx = await db.Database.BeginTransactionAsync();
            db.Assets.Add(asset);
            await SqliteGuard.SaveChangesAsync(db);

            db.AssetHistory.Add(new AssetHistory
            {
                AssetId = asset.Id,
                Username = actor.Username,
                Action = HistoryAction.Created,
                Timestamp = now
            });
            if (!string.IsNullOrWhiteSpace(asset.AssignedUserName))
            {
                db.AssetHistory.Add(new AssetHistory
                {
                    AssetId = asset.Id,
                    Username = actor.Username,
                    Action = HistoryAction.Assigned,
                    FieldName = "Assigned User",
                    OldValue = "Unassigned",
                    NewValue = asset.AssignedUserName,
                    Timestamp = now
                });
            }
            await SqliteGuard.SaveChangesAsync(db);
            await tx.CommitAsync();
            await IpAssetBridge.AfterAssetSavedAsync(db, asset, previousIp: null, actor);
            return await GetAsync(asset.Id, history: false);
        });

    public Task<AssetDetailDto> UpdateAsync(int id, SaveAssetRequest req, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var asset = await db.Assets
                .Include(a => a.Category)
                .Include(a => a.Location)
                .Include(a => a.AssignedUser)
                .FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new AppException(404, "not_found", "Asset not found.");

            if (asset.Version != req.Version)
                throw new AppException(409, "conflict",
                    "This asset was modified by another user. Please refresh the asset before saving.");

            var errors = await ValidateAsync(req, excludeId: id);
            if (errors.Count > 0)
                throw new AppException(400, "validation", "Please correct the highlighted fields.", errors);

            var now = DateTime.UtcNow;
            var changes = new List<AssetHistory>();

            void Track(string field, string? oldVal, string? newVal, HistoryAction action = HistoryAction.Updated)
            {
                oldVal = string.IsNullOrWhiteSpace(oldVal) ? null : oldVal;
                newVal = string.IsNullOrWhiteSpace(newVal) ? null : newVal;
                if (string.Equals(oldVal, newVal, StringComparison.Ordinal))
                    return;
                changes.Add(new AssetHistory
                {
                    AssetId = asset.Id,
                    Username = actor.Username,
                    Action = action,
                    FieldName = field,
                    OldValue = oldVal ?? "",
                    NewValue = newVal ?? "",
                    Timestamp = now
                });
            }

            var oldTag = asset.AssetTag;
            var oldSerial = asset.SerialNumber;
            var oldHost = asset.Hostname;
            var oldIp = asset.IpAddress;
            var oldStatus = asset.Status.Display();
            var oldLoc = asset.Location?.Name;
            var oldAssigned = asset.AssignedUser?.Name ?? asset.AssignedUserName ?? "Unassigned";
            var oldWasAssigned = !string.IsNullOrWhiteSpace(asset.AssignedUserName) || asset.AssignedUserId != null;

            var users = await LoadUserKeysAsync();
            Mapping.Apply(asset, req);
            UserNameResolver.ApplyTo(asset, req.AssignedUserId, req.AssignedUserName, users);

            Track("Asset ID", oldTag, asset.AssetTag);
            Track("Serial Number", oldSerial, asset.SerialNumber);
            Track("Hostname", oldHost, asset.Hostname);
            Track("IP Address", oldIp, asset.IpAddress);
            Track("Status", oldStatus, asset.Status.Display());

            string? newLoc = null;
            if (asset.LocationId is { } lid)
                newLoc = await db.Locations.AsNoTracking().Where(l => l.Id == lid).Select(l => l.Name).FirstOrDefaultAsync();
            Track("Location", oldLoc, newLoc);

            var newAssigned = string.IsNullOrWhiteSpace(asset.AssignedUserName) ? "Unassigned" : asset.AssignedUserName;
            var assignAction = string.IsNullOrWhiteSpace(asset.AssignedUserName) ? HistoryAction.Returned : HistoryAction.Assigned;
            if (!string.Equals(oldAssigned, newAssigned, StringComparison.Ordinal))
                Track("Assigned User", oldAssigned, newAssigned, assignAction);

            if (string.IsNullOrWhiteSpace(asset.AssignedUserName))
                asset.AssignedDate = null;
            else if (!oldWasAssigned)
                asset.AssignedDate = now;

            asset.Version++;
            asset.UpdatedAt = now;
            if (asset.NeedsReview)
                asset.NeedsReview = false;
            db.AssetHistory.AddRange(changes);
            await SqliteGuard.SaveChangesAsync(db);
            await IpAssetBridge.AfterAssetSavedAsync(db, asset, oldIp, actor);
            return await GetAsync(asset.Id, history: false);
        });

    public Task DeleteAsync(int id, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new AppException(404, "not_found", "Asset not found.");
            var ip = asset.IpAddress;
            var assetId = asset.Id;

            db.AssetHistory.Add(new AssetHistory
            {
                AssetId = asset.Id,
                Username = actor.Username,
                Action = HistoryAction.Deleted,
                FieldName = "Asset ID",
                OldValue = asset.AssetTag,
                NewValue = "",
                Timestamp = DateTime.UtcNow
            });
            db.Assets.Remove(asset);
            await SqliteGuard.SaveChangesAsync(db);
            await IpAssetBridge.AfterAssetDeletedAsync(db, ip, assetId, actor);
        });

    public async Task<DashboardDto> DashboardAsync()
    {
        var grouped = await db.Assets.AsNoTracking()
            .Where(a => !a.IsTemporary && !a.NeedsReview)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int CountOf(AssetStatus s) => grouped.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var byCategory = await db.Categories.AsNoTracking()
            .Select(c => new CategoryCountDto
            {
                CategoryId = c.Id,
                Name = c.Name,
                Count = db.Assets.Count(a => a.CategoryId == c.Id && !a.IsTemporary && !a.NeedsReview)
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var soon = DateTime.UtcNow.Date.AddDays(60);
        var today = DateTime.UtcNow.Date;
        var warranty = await db.Assets.AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.Location)
            .Include(a => a.AssignedUser)
            .Where(a => a.WarrantyExpiry != null
                        && a.WarrantyExpiry >= today
                        && a.WarrantyExpiry <= soon
                        && !a.IsTemporary
                        && !a.NeedsReview
                        && a.Status != AssetStatus.Retired
                        && a.Status != AssetStatus.Disposed)
            .OrderBy(a => a.WarrantyExpiry)
            .Take(15)
            .ToListAsync();

        var recent = await db.AssetHistory.AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(12)
            .ToListAsync();

        var names = await ResolveHistoryNames(recent);

        return new DashboardDto
        {
            TotalAssets = grouped.Sum(x => x.Count),
            InUse = CountOf(AssetStatus.InUse),
            InStock = CountOf(AssetStatus.InStock),
            UnderRepair = CountOf(AssetStatus.UnderRepair),
            Damaged = CountOf(AssetStatus.Damaged),
            Standby = CountOf(AssetStatus.Standby),
            ByCategory = byCategory,
            RecentActivity = recent.Select(h => Mapping.ToDto(h, names.GetValueOrDefault(h.Username))).ToList(),
            WarrantyExpiringSoon = warranty.Select(Mapping.ToListDto).ToList(),
            PendingCount = (await PendingAsync()).Count
        };
    }

    public async Task<List<PendingDetailDto>> PendingAsync()
    {
        var ips = await db.IpRecords.AsNoTracking()
            .Where(x => x.Status == IpStatus.Used && !x.IsTemporary)
            .Select(x => new { x.Id, x.Address, x.AssignedUser, x.AssignedDevice, x.MacAddress })
            .ToListAsync();
        var assets = await db.Assets.AsNoTracking()
            .Where(a => !a.IsTemporary)
            .Select(a => new
            {
                a.Id,
                a.AssetTag,
                a.IpAddress,
                a.SerialNumber,
                a.Hostname,
                a.Manufacturer,
                a.Model,
                a.MacAddress,
                a.NeedsReview,
                a.MeLogon,
                Assigned = a.AssignedUser != null ? a.AssignedUser.Name : a.AssignedUserName
            })
            .ToListAsync();

        var byIp = new Dictionary<string, List<(int Id, string Tag, string? Serial, string? Host, string? Mfr, string? Model, string? Mac, string? User)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assets)
        {
            if (!IpAddressService.TryNormalize(a.IpAddress, out var ip, out var third, out _) || third is < 101 or > 107)
                continue;
            if (!byIp.TryGetValue(ip, out var list))
            {
                list = [];
                byIp[ip] = list;
            }
            list.Add((a.Id, a.AssetTag, a.SerialNumber, a.Hostname, a.Manufacturer, a.Model, a.MacAddress, a.Assigned));
        }

        var pending = new List<PendingDetailDto>();
        var seen = new HashSet<int>();

        foreach (var (ip, linked) in byIp)
        {
            if (linked.Count < 2) continue;
            var rec = ips.FirstOrDefault(x => string.Equals(x.Address, ip, StringComparison.OrdinalIgnoreCase));
            pending.Add(new PendingDetailDto
            {
                Kind = "Duplicate IP",
                AssetId = linked[0].Id,
                IpId = rec?.Id,
                Address = ip,
                AssignedUser = rec?.AssignedUser ?? linked[0].User,
                Hostname = rec?.AssignedDevice ?? linked[0].Host,
                AssetTag = string.Join(", ", linked.Select(x => x.Tag)),
                Missing = $"{linked.Count} inventory assets share this IP"
            });
        }

        foreach (var rec in ips)
        {
            if (!byIp.TryGetValue(rec.Address, out var linked) || linked.Count == 0)
            {
                pending.Add(new PendingDetailDto
                {
                    Kind = "Missing inventory",
                    IpId = rec.Id,
                    Address = rec.Address,
                    AssignedUser = rec.AssignedUser,
                    Hostname = rec.AssignedDevice,
                    MacAddress = rec.MacAddress,
                    Missing = "Inventory asset"
                });
                continue;
            }

            foreach (var a in linked)
            {
                seen.Add(a.Id);
                var issues = Gaps(a.Serial, a.Host, a.Mfr, a.Model, a.User);
                issues.AddRange(Mismatches(rec.AssignedUser, rec.AssignedDevice, rec.MacAddress, a.User, a.Host, a.Mac));
                if (issues.Count == 0) continue;
                var hasGap = issues.Exists(x => x is "Serial" or "Hostname" or "Make/model" or "Assigned user");
                var hasMis = issues.Exists(x => x.Contains('≠') || x.Contains("share", StringComparison.OrdinalIgnoreCase));
                pending.Add(new PendingDetailDto
                {
                    Kind = hasGap && hasMis ? "Incomplete / mismatch" : hasMis ? "Mismatch" : "Incomplete asset",
                    AssetId = a.Id,
                    IpId = rec.Id,
                    AssetTag = a.Tag,
                    Address = rec.Address,
                    AssignedUser = a.User ?? rec.AssignedUser,
                    Hostname = a.Host ?? rec.AssignedDevice,
                    MacAddress = a.Mac ?? rec.MacAddress,
                    Missing = string.Join(", ", issues)
                });
            }
        }

        foreach (var a in assets)
        {
            if (seen.Contains(a.Id)) continue;
            if (!IpAddressService.TryNormalize(a.IpAddress, out var ip, out var third, out _) || third is < 101 or > 107)
                continue;
            var missing = Gaps(a.SerialNumber, a.Hostname, a.Manufacturer, a.Model, a.Assigned);
            if (missing.Count == 0) continue;
            pending.Add(new PendingDetailDto
            {
                Kind = "Incomplete asset",
                AssetId = a.Id,
                AssetTag = a.AssetTag,
                Address = ip,
                AssignedUser = a.Assigned,
                Hostname = a.Hostname,
                MacAddress = a.MacAddress,
                Missing = string.Join(", ", missing)
            });
        }

        foreach (var a in assets.Where(x => x.NeedsReview))
        {
            var extra = string.IsNullOrWhiteSpace(a.MeLogon)
                ? "Confirm new ManageEngine PC"
                : "Confirm new ManageEngine PC · ME logon " + a.MeLogon;
            var existing = pending.FirstOrDefault(p => p.AssetId == a.Id);
            if (existing is not null)
            {
                existing.Kind = "Confirm ME import";
                existing.Missing = string.IsNullOrWhiteSpace(existing.Missing) ? extra : extra + "; " + existing.Missing;
            }
            else
            {
                pending.Add(new PendingDetailDto
                {
                    Kind = "Confirm ME import",
                    AssetId = a.Id,
                    AssetTag = a.AssetTag,
                    Address = a.IpAddress,
                    AssignedUser = a.Assigned,
                    Hostname = a.Hostname,
                    MacAddress = a.MacAddress,
                    Missing = extra
                });
            }
        }

        return pending
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Address)
            .ThenBy(x => x.AssetTag)
            .ToList();
    }

    public Task ConfirmReviewAsync(int id, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new AppException(404, "not_found", "Asset not found.");
            if (!asset.NeedsReview) return;
            asset.NeedsReview = false;
            asset.Version++;
            asset.UpdatedAt = DateTime.UtcNow;
            db.AssetHistory.Add(new AssetHistory
            {
                AssetId = asset.Id,
                Username = actor.Username,
                Action = HistoryAction.Updated,
                FieldName = "ManageEngine",
                OldValue = "Pending confirm",
                NewValue = "Confirmed",
                Timestamp = DateTime.UtcNow
            });
            await SqliteGuard.SaveChangesAsync(db);
        });

    private static List<string> Gaps(string? serial, string? host, string? mfr, string? model, string? user)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(serial)) missing.Add("Serial");
        if (string.IsNullOrWhiteSpace(host)) missing.Add("Hostname");
        if (string.IsNullOrWhiteSpace(mfr) && string.IsNullOrWhiteSpace(model)) missing.Add("Make/model");
        if (string.IsNullOrWhiteSpace(user)) missing.Add("Assigned user");
        return missing;
    }

    private static List<string> Mismatches(
        string? ipUser, string? ipHost, string? ipMac,
        string? assetUser, string? assetHost, string? assetMac)
    {
        var issues = new List<string>();
        if (BothFilled(ipUser, assetUser) && !SameText(ipUser, assetUser))
            issues.Add("IP user ≠ inventory user");
        if (BothFilled(ipHost, assetHost) && !SameText(ipHost, assetHost))
            issues.Add("Hostname ≠ IP device");
        if (BothFilled(ipMac, assetMac) && !SameMac(ipMac, assetMac))
            issues.Add("MAC ≠ IP MAC");
        return issues;
    }

    private static bool BothFilled(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b);

    private static bool SameText(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameMac(string? a, string? b)
    {
        static string Digits(string? s) =>
            new(s?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
        var x = Digits(a);
        var y = Digits(b);
        return x.Length > 0 && x == y;
    }

    private async Task<List<string>> ValidateAsync(SaveAssetRequest req, int? excludeId)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(req.AssetTag))
            errors.Add("Asset ID is required.");
        else
        {
            var tag = req.AssetTag.Trim();
            var exists = await db.Assets.AnyAsync(a => a.AssetTag == tag && a.Id != excludeId);
            if (exists)
                errors.Add($"Asset ID '{tag}' already exists.");
        }

        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            errors.Add("Category is required.");

        var serial = Mapping.Clean(req.SerialNumber);
        if (serial is not null)
        {
            var dup = await db.Assets.AnyAsync(a => a.SerialNumber == serial && a.Id != excludeId);
            if (dup)
                errors.Add($"Serial number '{serial}' is already in use.");
        }

        if (req.LocationId is { } loc && !await db.Locations.AnyAsync(l => l.Id == loc))
            errors.Add("Location is not valid.");

        if (req.AssignedUserId is { } uid && !await db.Users.AnyAsync(u => u.Id == uid))
            errors.Add("Assigned user is not valid.");

        return errors;
    }

    private async Task<List<HistoryDto>> LoadHistoryAsync(int assetId)
    {
        var rows = await db.AssetHistory.AsNoTracking()
            .Where(h => h.AssetId == assetId)
            .OrderByDescending(h => h.Timestamp)
            .ThenByDescending(h => h.Id)
            .ToListAsync();
        var names = await ResolveHistoryNames(rows);
        return rows.Select(h => Mapping.ToDto(h, names.GetValueOrDefault(h.Username))).ToList();
    }

    private async Task<Dictionary<string, string>> ResolveHistoryNames(List<AssetHistory> rows)
    {
        var keys = rows.Select(h => h.Username).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => keys.Contains(u.Username))
            .Select(u => new { u.Username, u.Name })
            .ToListAsync();
        var map = users.ToDictionary(u => u.Username, u => u.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var k in keys)
            map.TryAdd(k, Mapping.ShortName(k));
        return map;
    }

    public Task<int> BulkPatchAsync(BulkEditRequest req, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            if (req.Ids.Count == 0)
                throw new AppException(400, "validation", "Select at least one asset.");

            var now = DateTime.UtcNow;
            var count = 0;
            var users = await LoadUserKeysAsync();
            var setAssignment = req.SetAssignedUser || req.AssignedUserId is not null || req.AssignedUserName is not null;
            var locNames = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name);
            var ipChanges = new List<(string? OldIp, Asset Asset)>();

            foreach (var id in req.Ids.Distinct())
            {
                var asset = await db.Assets.Include(a => a.Location).Include(a => a.AssignedUser).FirstOrDefaultAsync(a => a.Id == id);
                if (asset is null) continue;

                void Track(string field, string? oldVal, string? newVal, HistoryAction action = HistoryAction.Updated)
                {
                    oldVal = string.IsNullOrWhiteSpace(oldVal) ? null : oldVal;
                    newVal = string.IsNullOrWhiteSpace(newVal) ? null : newVal;
                    if (string.Equals(oldVal, newVal, StringComparison.Ordinal)) return;
                    db.AssetHistory.Add(new AssetHistory
                    {
                        AssetId = asset.Id,
                        Username = actor.Username,
                        Action = action,
                        FieldName = field,
                        OldValue = oldVal ?? "",
                        NewValue = newVal ?? "",
                        Timestamp = now
                    });
                }

                if (req.Status is { } st && asset.Status != st)
                {
                    Track("Status", asset.Status.Display(), st.Display());
                    asset.Status = st;
                }
                if (req.SetLocation)
                {
                    locNames.TryGetValue(asset.LocationId ?? 0, out var oldLoc);
                    oldLoc ??= asset.Location?.Name;
                    string? newLoc = null;
                    if (req.LocationId is { } newId)
                        locNames.TryGetValue(newId, out newLoc);
                    Track("Location", oldLoc, newLoc);
                    asset.LocationId = req.LocationId;
                }
                if (setAssignment)
                {
                    var oldAssigned = asset.AssignedUser?.Name ?? asset.AssignedUserName ?? "Unassigned";
                    var wasAssigned = !string.IsNullOrWhiteSpace(asset.AssignedUserName) || asset.AssignedUserId != null;
                    UserNameResolver.ApplyTo(asset, req.AssignedUserId, req.AssignedUserName, users);
                    var newAssigned = string.IsNullOrWhiteSpace(asset.AssignedUserName) ? "Unassigned" : asset.AssignedUserName;
                    var action = string.IsNullOrWhiteSpace(asset.AssignedUserName) ? HistoryAction.Returned : HistoryAction.Assigned;
                    Track("Assigned User", oldAssigned, newAssigned, action);
                    if (string.IsNullOrWhiteSpace(asset.AssignedUserName))
                        asset.AssignedDate = null;
                    else if (!wasAssigned)
                        asset.AssignedDate = now;
                }
                if (req.Designation is not null)
                {
                    Track("Designation", asset.Designation, req.Designation);
                    asset.Designation = Mapping.Clean(req.Designation);
                }
                if (req.Hostname is not null)
                {
                    Track("Hostname", asset.Hostname, req.Hostname);
                    asset.Hostname = Mapping.Clean(req.Hostname);
                }
                if (req.IpAddress is not null)
                {
                    Track("IP Address", asset.IpAddress, req.IpAddress);
                    var oldIp = asset.IpAddress;
                    asset.IpAddress = Mapping.Clean(req.IpAddress);
                    ipChanges.Add((oldIp, asset));
                }
                if (req.Domain is not null)
                {
                    Track("Domain", asset.Domain, req.Domain);
                    asset.Domain = Mapping.Clean(req.Domain);
                }
                if (req.MfaEnabled is not null)
                {
                    Track("MFA Enabled", asset.MfaEnabled, req.MfaEnabled);
                    asset.MfaEnabled = Mapping.YesNo(req.MfaEnabled);
                }
                if (req.AdminRights is not null)
                {
                    Track("Admin Rights", asset.AdminRights, req.AdminRights);
                    asset.AdminRights = Mapping.YesNo(req.AdminRights);
                }
                if (req.UsbAccess is not null)
                {
                    Track("USB Access", asset.UsbAccess, req.UsbAccess);
                    asset.UsbAccess = Mapping.YesNo(req.UsbAccess);
                }
                if (req.SetLastConnected)
                {
                    Track("Last Connected", FmtDate(asset.LastConnected), FmtDate(req.LastConnected));
                    asset.LastConnected = req.LastConnected?.Date;
                }
                if (req.CollectBy is not null)
                {
                    Track("Collect By", asset.CollectBy, req.CollectBy);
                    asset.CollectBy = Mapping.Clean(req.CollectBy);
                }

                asset.Version++;
                asset.UpdatedAt = now;
                count++;
            }
            await SqliteGuard.SaveChangesAsync(db);
            foreach (var (oldIp, asset) in ipChanges)
                await IpAssetBridge.AfterAssetSavedAsync(db, asset, oldIp, actor);
            return count;
        });

    public Task<int> BulkAddAsync(BulkAddRequest req, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
                throw new AppException(400, "validation", "Category is required.");
            var lines = (req.Lines ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
                throw new AppException(400, "validation", "Paste at least one line: SERIAL or SERIAL,HOSTNAME,IP,USER.");

            var existing = await db.Assets.AsNoTracking()
                .Select(a => new { a.AssetTag, a.SerialNumber })
                .ToListAsync();
            var tags = new HashSet<string>(existing.Select(a => a.AssetTag), StringComparer.OrdinalIgnoreCase);
            var serials = new HashSet<string>(existing.Where(a => a.SerialNumber != null).Select(a => a.SerialNumber!), StringComparer.OrdinalIgnoreCase);
            var users = await LoadUserKeysAsync();

            var now = DateTime.UtcNow;
            var created = new List<Asset>();
            foreach (var line in lines)
            {
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                var serial = Mapping.Clean(parts[0]);
                if (serial is null)
                    throw new AppException(400, "validation", $"Missing serial on line: {line}");
                if (!serials.Add(serial) || !tags.Add(serial))
                    throw new AppException(400, "validation", $"Serial '{serial}' already exists.");

                var hostname = parts.Length > 1 ? Mapping.Clean(parts[1]) : null;
                var ip = parts.Length > 2 ? Mapping.Clean(parts[2]) : null;
                var user = parts.Length > 3 ? Mapping.Clean(string.Join(",", parts.Skip(3))) : null;

                var asset = new Asset
                {
                    AssetTag = serial,
                    SerialNumber = serial,
                    CategoryId = req.CategoryId,
                    LocationId = req.LocationId,
                    Status = req.Status,
                    Manufacturer = Mapping.Clean(req.Manufacturer),
                    Model = Mapping.Clean(req.Model),
                    Designation = Mapping.Clean(req.Designation),
                    Domain = Mapping.Clean(req.Domain),
                    Hostname = hostname,
                    IpAddress = ip,
                    AssignedDate = user is null ? null : now,
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                UserNameResolver.ApplyTo(asset, null, user, users);
                created.Add(asset);
                db.Assets.Add(asset);
            }

            await SqliteGuard.SaveChangesAsync(db);

            foreach (var asset in created)
            {
                db.AssetHistory.Add(new AssetHistory
                {
                    AssetId = asset.Id,
                    Username = actor.Username,
                    Action = HistoryAction.Created,
                    Timestamp = now
                });
            }
            await SqliteGuard.SaveChangesAsync(db);
            return created.Count;
        });

    public Task<int> RenumberByIpAsync(List<int>? ids, CurrentUser actor) =>
        writeLock.WriteAsync(async () =>
        {
            var query = db.Assets.AsQueryable();
            if (ids is { Count: > 0 })
                query = query.Where(a => ids.Contains(a.Id));
            var rows = await query.ToListAsync();
            var ordered = rows
                .Select(a => (Asset: a, Key: IpSortKey(a.IpAddress)))
                .OrderBy(x => x.Key.HasValue ? 0 : 1)
                .ThenBy(x => x.Key ?? 0)
                .ThenBy(x => x.Asset.Hostname)
                .Select(x => x.Asset)
                .ToList();

            var now = DateTime.UtcNow;
            var n = 1;
            foreach (var a in ordered)
            {
                if (a.SrNo != n)
                {
                    db.AssetHistory.Add(new AssetHistory
                    {
                        AssetId = a.Id,
                        Username = actor.Username,
                        Action = HistoryAction.Updated,
                        FieldName = "Sr No",
                        OldValue = a.SrNo?.ToString() ?? "",
                        NewValue = n.ToString(),
                        Timestamp = now
                    });
                    a.SrNo = n;
                    a.Version++;
                    a.UpdatedAt = now;
                }
                n++;
            }
            await SqliteGuard.SaveChangesAsync(db);
            return ordered.Count;
        });

    public async Task<List<DuplicateGroupDto>> DuplicateSerialsAsync()
    {
        var rows = await db.Assets.AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.Location)
            .Where(a => a.SerialNumber != null && a.SerialNumber != "")
            .ToListAsync();
        return rows
            .GroupBy(a => a.SerialNumber!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroupDto
            {
                SerialNumber = g.Key,
                Assets = g.Select(Mapping.ToListDto).ToList()
            })
            .OrderBy(g => g.SerialNumber)
            .ToList();
    }

    public static ulong? IpSortKey(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var t = ip.Trim();
        if (t.Equals("WIFI", StringComparison.OrdinalIgnoreCase) || t == "0")
            return null;
        var parts = t.Split('.');
        if (parts.Length != 4) return null;
        ulong v = 0;
        foreach (var p in parts)
        {
            if (!byte.TryParse(p, out var b)) return null;
            v = (v << 8) | b;
        }
        return v;
    }

    private async Task<List<(int Id, string Name, string Username)>> LoadUserKeysAsync() =>
        (await db.Users.AsNoTracking().Select(u => new { u.Id, u.Name, u.Username }).ToListAsync())
        .Select(u => (u.Id, u.Name, u.Username))
        .ToList();

    private static string? FmtDate(DateTime? d) => d?.ToString("dd MMM yyyy");
}
