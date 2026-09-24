using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Core.Services;

public class AdDirectoryService(AppDbContext db, IWriteLock writeLock)
{
    public async Task<AdImportPreviewDto> PreviewAsync(Stream excel) =>
        await BuildPlanAsync(excel);

    public Task<AdImportResultDto> ImportAsync(Stream excel) =>
        writeLock.WriteAsync(async () =>
        {
            var plan = await BuildPlanAsync(excel);
            if (!plan.CanImport)
                throw new AppException(400, "validation", plan.Summary, plan.Issues);

            var existing = await db.AdDirectory.ToListAsync();
            var bySam = existing.ToDictionary(x => x.Sam, StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var updated = 0;

            foreach (var line in plan.Lines)
            {
                if (line.Action is not "Add" and not "Update") continue;
                if (!bySam.TryGetValue(line.Sam, out var row))
                {
                    row = new AdDirectoryEntry { Sam = line.Sam };
                    db.AdDirectory.Add(row);
                    bySam[line.Sam] = row;
                    added++;
                }
                else
                    updated++;

                row.Name = line.Name;
                row.Email = line.Email;
                row.Department = line.Department;
                row.EmployeeId = line.EmployeeId;
                row.IsActive = line.Status.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
            }

            await SqliteGuard.SaveChangesAsync(db);
            var link = await new AdPersonLinker(db).LinkUnassignedFromMeLogonAsync();
            return new AdImportResultDto
            {
                Added = added,
                Updated = updated,
                Skipped = plan.SkipCount,
                PeopleCreated = link.PeopleCreated,
                AssetsLinked = link.AssetsLinked,
                Summary = Summary(added, updated, plan.SkipCount, link.PeopleCreated, link.AssetsLinked)
            };
        });

    public async Task<List<AdUserDto>> ListAsync()
    {
        var rows = await db.AdDirectory.AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Sam)
            .ToListAsync();
        var pav = new HashSet<string>(
            await db.Users.AsNoTracking()
                .Where(u => u.SamAccount != null && u.SamAccount != "")
                .Select(u => u.SamAccount!)
                .ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        return rows.Select(x => new AdUserDto
        {
            Id = x.Id,
            Sam = x.Sam,
            Name = x.Name,
            Email = x.Email,
            Department = x.Department,
            EmployeeId = x.EmployeeId,
            IsActive = x.IsActive,
            InPav = pav.Contains(x.Sam)
        }).ToList();
    }

    public Task<UserDto?> EnsurePersonFromTypedAsync(string raw) =>
        writeLock.WriteAsync(async () =>
        {
            var linker = new AdPersonLinker(db);
            var ad = linker.Find(raw);
            if (ad is null) return null;
            var cache = await linker.LoadPeopleCacheAsync();
            var (user, _) = await linker.EnsurePersonAsync(ad, cache);
            return Mapping.ToDto(user);
        });

    public Task<AdUserDto> SaveAsync(int? id, SaveAdUserRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            var sam = AdLogon.Normalize(req.Sam);
            if (sam is null)
                throw new AppException(400, "validation", "User ID is required.");
            var name = Mapping.Clean(req.Name);
            if (name is null)
                throw new AppException(400, "validation", "Name is required.");

            AdDirectoryEntry row;
            string? oldSam = null;
            if (id is { } eid)
            {
                row = await db.AdDirectory.FirstOrDefaultAsync(x => x.Id == eid)
                    ?? throw new AppException(404, "not_found", "AD user not found.");
                oldSam = row.Sam;
            }
            else
            {
                row = new AdDirectoryEntry();
                db.AdDirectory.Add(row);
            }

            var taken = await db.AdDirectory.AnyAsync(x => x.Id != row.Id && x.Sam.ToLower() == sam);
            if (taken)
                throw new AppException(400, "validation", "That user ID is already in AD users.");

            var people = await db.Users.Where(u => !u.CanSignIn).ToListAsync();
            var linked = people.Where(u =>
                    (!string.IsNullOrWhiteSpace(u.SamAccount)
                     && (u.SamAccount.Equals(sam, StringComparison.OrdinalIgnoreCase)
                         || (oldSam is not null && u.SamAccount.Equals(oldSam, StringComparison.OrdinalIgnoreCase))))
                    || (string.IsNullOrWhiteSpace(u.SamAccount)
                        && u.Username.Equals(oldSam ?? sam, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var linkedIds = linked.Select(u => u.Id).ToHashSet();
            var conflict = await db.Users.AnyAsync(u =>
                !linkedIds.Contains(u.Id) && u.SamAccount != null && u.SamAccount != "" && u.SamAccount.ToLower() == sam);
            if (conflict)
                throw new AppException(400, "validation", $"User id '{sam}' is already assigned to a different PAV user.");

            row.Sam = sam;
            row.Name = name;
            row.Email = Mapping.Clean(req.Email);
            row.Department = Mapping.Clean(req.Department);
            row.EmployeeId = Mapping.Clean(req.EmployeeId);
            row.IsActive = req.IsActive;

            foreach (var person in linked)
            {
                person.SamAccount = sam;
                person.Name = name;
                if (row.Email is not null) person.Email = row.Email;
                if (row.Department is not null) person.Department = row.Department;
                if (row.EmployeeId is not null) person.EmployeeId = row.EmployeeId;
                person.IsActive = row.IsActive;
            }

            await SqliteGuard.SaveChangesAsync(db);
            return new AdUserDto
            {
                Id = row.Id,
                Sam = row.Sam,
                Name = row.Name,
                Email = row.Email,
                Department = row.Department,
                EmployeeId = row.EmployeeId,
                IsActive = row.IsActive,
                InPav = linked.Count > 0
            };
        });

    public Task DeleteAsync(int id) =>
        writeLock.WriteAsync(async () =>
        {
            var row = await db.AdDirectory.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new AppException(404, "not_found", "AD user not found.");
            db.AdDirectory.Remove(row);
            await SqliteGuard.SaveChangesAsync(db);
            return 0;
        });

    private async Task<AdImportPreviewDto> BuildPlanAsync(Stream excel)
    {
        var parsed = Parse(excel);
        var preview = new AdImportPreviewDto();
        if (parsed.Count == 0)
        {
            preview.Summary = "No AD user rows found. Need a SAM Account Name column (ADMP All Users export).";
            preview.Issues.Add("Open the ADMP All Users workbook. The header row includes SAM Account Name.");
            return preview;
        }

        var existing = await db.AdDirectory.AsNoTracking()
            .Select(x => x.Sam)
            .ToListAsync();
        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var seen = new Dictionary<string, AdRow>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<AdImportLineDto>();
        var dup = 0;
        foreach (var row in parsed)
        {
            if (seen.ContainsKey(row.Sam))
            {
                dup++;
                lines.Add(new AdImportLineDto
                {
                    Action = "Skip",
                    Sam = row.Sam,
                    Name = row.Name,
                    Email = row.Email,
                    Department = row.Department,
                    EmployeeId = row.EmployeeId,
                    Status = row.IsActive ? "Enabled" : "Disabled",
                    Notes = "Duplicate user id in this file — first row kept"
                });
                continue;
            }
            seen[row.Sam] = row;
            var isUpdate = have.Contains(row.Sam);
            lines.Add(new AdImportLineDto
            {
                Action = isUpdate ? "Update" : "Add",
                Sam = row.Sam,
                Name = row.Name,
                Email = row.Email,
                Department = row.Department,
                EmployeeId = row.EmployeeId,
                Status = row.IsActive ? "Enabled" : "Disabled"
            });
        }

        var pendingLogons = await db.Assets.AsNoTracking()
            .Where(a => a.AssignedUserId == null && a.MeLogon != null && a.MeLogon != "")
            .Select(a => a.MeLogon)
            .ToListAsync();
        var linkable = 0;
        foreach (var raw in pendingLogons)
        {
            var id = AdLogon.Normalize(raw);
            if (id is not null && seen.ContainsKey(id))
                linkable++;
        }

        preview.Lines = lines;
        preview.AddCount = lines.Count(x => x.Action == "Add");
        preview.UpdateCount = lines.Count(x => x.Action == "Update");
        preview.SkipCount = lines.Count(x => x.Action == "Skip") + parsed.Count(x => x.Skipped);
        preview.LinkAssetCount = linkable;
        preview.CanImport = preview.AddCount + preview.UpdateCount > 0;
        preview.Summary =
            $"{preview.AddCount} user ids to store, {preview.UpdateCount} already in PAV (refresh), " +
            $"{preview.SkipCount} skipped. After Apply, {linkable} PCs with a last logon and no assigned user can be linked.";
        if (dup > 0)
            preview.Issues.Add($"{dup} duplicate user ids in the file. The first row for each id is kept.");
        var skippedService = parsed.Count(x => x.Skipped);
        if (skippedService > 0)
            preview.Issues.Add($"{skippedService} service / junk accounts skipped (MSOL, krbtgt, Guest, machine accounts).");
        return preview;
    }

    private static string Summary(int added, int updated, int skipped, int people, int assets)
    {
        var parts = new List<string> { $"{added} user ids stored", $"{updated} refreshed" };
        if (skipped > 0) parts.Add($"{skipped} skipped");
        if (people > 0) parts.Add($"{people} people added to Users");
        if (assets > 0) parts.Add($"{assets} PCs linked from last logon");
        return string.Join(". ", parts) + ".";
    }

    private sealed class AdRow
    {
        public string Sam { get; set; } = "";
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Department { get; set; }
        public string? EmployeeId { get; set; }
        public bool IsActive { get; set; }
        public bool Skipped { get; set; }
    }

    private static List<AdRow> Parse(Stream excel)
    {
        using var wb = new XLWorkbook(excel);
        IXLRangeRow? header = null;
        IXLWorksheet? sheet = null;
        foreach (var ws in wb.Worksheets)
        {
            var scan = ws.RangeUsed();
            if (scan is null) continue;
            foreach (var row in scan.RowsUsed())
            {
                foreach (var cell in row.CellsUsed())
                {
                    var t = cell.GetString().Trim();
                    if (t.Equals("SAM Account Name", StringComparison.OrdinalIgnoreCase))
                    {
                        header = row;
                        sheet = ws;
                        break;
                    }
                }
                if (header is not null) break;
            }
            if (header is not null) break;
        }

        if (header is null || sheet is null)
            return [];

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in header.CellsUsed())
        {
            var name = cell.GetString().Trim();
            if (name.Length > 0 && !map.ContainsKey(name))
                map[name] = cell.Address.ColumnNumber;
        }

        int? Col(params string[] names)
        {
            foreach (var n in names)
            {
                if (map.TryGetValue(n, out var c)) return c;
                foreach (var kv in map)
                {
                    if (kv.Key.Equals(n, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                    if (kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                }
            }
            return null;
        }

        var cSam = Col("SAM Account Name") ?? throw new AppException(400, "validation",
            "This is not an AD All Users report. Need a SAM Account Name column.");
        var cDisp = Col("Display Name");
        var cFull = Col("Full Name");
        var cFirst = Col("First Name");
        var cLast = Col("Last Name");
        var cEmail = Col("Email Address");
        var cDept = Col("Department");
        var cEmp = Col("Employee ID");
        var cInit = Col("Initials");
        var cStatus = Col("Account Status");
        var cDesc = Col("Description");
        var cDn = Col("Distinguished Name");

        string? Cell(IXLRangeRow row, int? col)
        {
            if (col is null) return null;
            var s = row.Cell(col.Value).GetFormattedString().Trim();
            return Blank(s);
        }

        var data = sheet.RangeUsed()!;
        var rows = new List<AdRow>();
        foreach (var row in data.RowsUsed())
        {
            if (row.RowNumber() == header.RowNumber()) continue;
            var samRaw = Cell(row, cSam);
            var sam = AdLogon.Normalize(samRaw);
            var desc = Cell(row, cDesc);
            var dn = Cell(row, cDn);
            var disp = Cell(row, cDisp);
            if (sam is null) continue;
            if (IsService(sam, disp, desc, dn))
            {
                rows.Add(new AdRow { Sam = sam, Skipped = true });
                continue;
            }

            var full = Cell(row, cFull);
            var first = Cell(row, cFirst);
            var last = Cell(row, cLast);
            var name = disp ?? full ?? JoinName(first, last) ?? sam;
            var emp = Cell(row, cEmp);
            if (emp is null)
            {
                var initials = Cell(row, cInit);
                if (initials is not null && initials.All(char.IsDigit))
                    emp = initials;
            }

            var status = Cell(row, cStatus) ?? "";
            rows.Add(new AdRow
            {
                Sam = sam,
                Name = name,
                Email = Cell(row, cEmail),
                Department = Cell(row, cDept),
                EmployeeId = emp,
                IsActive = status.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
            });
        }
        return rows;
    }

    private static string? JoinName(string? first, string? last)
    {
        var t = $"{first} {last}".Trim();
        return t.Length == 0 ? null : t;
    }

    private static string? Blank(string? s)
    {
        var t = Mapping.Clean(s);
        if (t is null) return null;
        if (t is "-" or "--" or "NULL" or "null" or "N/A" or "n/a") return null;
        return t;
    }

    private static bool IsService(string sam, string? name, string? desc, string? dn)
    {
        if (sam.StartsWith('$')) return true;
        if (sam.StartsWith("MSOL_", StringComparison.OrdinalIgnoreCase)) return true;
        if (sam.StartsWith("krbtgt", StringComparison.OrdinalIgnoreCase)) return true;
        if (sam.Equals("Guest", StringComparison.OrdinalIgnoreCase)) return true;
        if (sam.Equals("Guest_Disable", StringComparison.OrdinalIgnoreCase)) return true;
        if (name is not null && name.Contains("ApplicationAccount", StringComparison.OrdinalIgnoreCase))
            return true;
        if (desc is not null && desc.Contains("Azure Active Directory Connect", StringComparison.OrdinalIgnoreCase))
            return true;
        if (dn is not null && dn.Contains("CN=Exchange Online", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}

public static class AdLogon
{
    public static string? Normalize(string? raw)
    {
        var t = Mapping.Clean(raw);
        if (t is null) return null;
        var parts = t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var s = p;
            if (s.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Administrator", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Windows User", StringComparison.OrdinalIgnoreCase) ||
                s is "-" or "'-")
                continue;
            var slash = s.LastIndexOf('\\');
            if (slash >= 0 && slash < s.Length - 1)
                s = s[(slash + 1)..];
            var at = s.IndexOf('@');
            if (at > 0)
                s = s[..at];
            s = s.Trim();
            if (s.Length == 0) continue;
            if (s.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Administrator", StringComparison.OrdinalIgnoreCase))
                continue;
            return s.ToLowerInvariant();
        }
        return null;
    }
}

public sealed class AdPersonLinker(AppDbContext db)
{
    public readonly record struct LinkResult(int PeopleCreated, int AssetsLinked);

    public Dictionary<string, AdDirectoryEntry> LoadDirectory() =>
        db.AdDirectory.AsNoTracking().ToList()
            .GroupBy(x => x.Sam, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public AdDirectoryEntry? Find(string? raw)
    {
        var dir = LoadDirectory();
        if (dir.Count == 0) return null;
        var sam = AdLogon.Normalize(raw);
        if (sam is not null && dir.TryGetValue(sam, out var bySam))
            return bySam;

        var key = Mapping.Clean(raw);
        if (key is null) return null;
        if (key.EndsWith(')'))
        {
            var open = key.LastIndexOf('(');
            if (open > 0)
            {
                var id = AdLogon.Normalize(key[(open + 1)..^1]);
                if (id is not null && dir.TryGetValue(id, out var byParen))
                    return byParen;
                key = Mapping.Clean(key[..open]) ?? key;
            }
        }

        var nameHits = dir.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.Name)
                        && string.Equals(x.Name.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return nameHits.Count == 1 ? nameHits[0] : null;
    }

    public string? Describe(string? meLogon, IReadOnlyDictionary<string, AdDirectoryEntry> directory)
    {
        var sam = AdLogon.Normalize(meLogon);
        if (sam is null) return null;
        if (!directory.TryGetValue(sam, out var ad)) return null;
        return ad.Sam + " → " + (ad.Name ?? ad.Sam);
    }

    public async Task<LinkResult> LinkUnassignedFromMeLogonAsync()
    {
        var directory = LoadDirectory();
        if (directory.Count == 0) return new LinkResult(0, 0);

        var assets = await db.Assets
            .Where(a => a.AssignedUserId == null && a.MeLogon != null && a.MeLogon != "")
            .ToListAsync();
        var people = 0;
        var linked = 0;
        var cache = await LoadPeopleCacheAsync();
        foreach (var asset in assets)
        {
            var n = await AssignAsync(asset, asset.MeLogon, directory, cache, onlyIfUnassigned: true);
            people += n.Created ? 1 : 0;
            linked += n.Linked ? 1 : 0;
        }
        if (people + linked > 0)
            await SqliteGuard.SaveChangesAsync(db);
        return new LinkResult(people, linked);
    }

    public async Task<(bool Created, bool Linked)> AssignAsync(
        Asset asset,
        string? meLogon,
        IReadOnlyDictionary<string, AdDirectoryEntry> directory,
        PeopleCache cache,
        bool onlyIfUnassigned)
    {
        if (onlyIfUnassigned && asset.AssignedUserId is not null)
            return (false, false);
        var sam = AdLogon.Normalize(meLogon);
        if (sam is null) return (false, false);
        if (!directory.TryGetValue(sam, out var ad))
            return (false, false);

        var (user, created) = await EnsurePersonAsync(ad, cache);
        if (asset.AssignedUserId == user.Id)
        {
            ApplyDirectory(user, ad);
            return (created, false);
        }
        asset.AssignedUserId = user.Id;
        asset.AssignedUserName = user.Name;
        return (created, true);
    }

    public sealed class PeopleCache
    {
        public Dictionary<string, User> BySam { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> TakenUsernames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PeopleCache> LoadPeopleCacheAsync()
    {
        var users = await db.Users.ToListAsync();
        var cache = new PeopleCache();
        foreach (var u in users)
        {
            cache.TakenUsernames.Add(u.Username);
            if (!string.IsNullOrWhiteSpace(u.SamAccount))
                cache.BySam[u.SamAccount] = u;
            else if (!u.CanSignIn)
                cache.BySam[u.Username] = u;
        }
        return cache;
    }

    public async Task<(User User, bool Created)> EnsurePersonAsync(AdDirectoryEntry ad, PeopleCache cache)
    {
        if (cache.BySam.TryGetValue(ad.Sam, out var hit))
        {
            if (!hit.CanSignIn)
                ApplyDirectory(hit, ad);
            return (hit, false);
        }

        var username = ad.Sam;
        if (!cache.TakenUsernames.Add(username))
            username = LookupService.NextPersonUsername(ad.Name ?? ad.Sam, cache.TakenUsernames);

        var person = new User
        {
            Name = Mapping.Clean(ad.Name) ?? ad.Sam,
            Username = username,
            SamAccount = ad.Sam,
            Email = ad.Email,
            Department = ad.Department,
            EmployeeId = ad.EmployeeId,
            PasswordHash = "!",
            Role = UserRole.Guest,
            IsActive = ad.IsActive,
            CanSignIn = false
        };
        db.Users.Add(person);
        await SqliteGuard.SaveChangesAsync(db);
        cache.BySam[ad.Sam] = person;
        return (person, true);
    }

    private static void ApplyDirectory(User user, AdDirectoryEntry ad)
    {
        user.SamAccount = ad.Sam;
        if (Mapping.Clean(ad.Name) is { } name)
            user.Name = name;
        if (ad.Email is not null) user.Email = ad.Email;
        if (ad.Department is not null) user.Department = ad.Department;
        if (ad.EmployeeId is not null) user.EmployeeId = ad.EmployeeId;
        user.IsActive = ad.IsActive;
    }
}
