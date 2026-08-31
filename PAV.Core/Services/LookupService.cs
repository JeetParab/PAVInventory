using Microsoft.EntityFrameworkCore;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;
using PAV.Core.Data;

namespace PAV.Core.Services;

public class LookupService(AppDbContext db, IWriteLock writeLock)

{
    public async Task<List<UserDto>> UsersAsync() => await UsersAsync(canSignIn: null);

    public async Task<List<UserDto>> PeopleAsync() => await UsersAsync(canSignIn: false);

    public async Task<List<UserDto>> SignInUsersAsync() => await UsersAsync(canSignIn: true);

    private async Task<List<UserDto>> UsersAsync(bool? canSignIn)
    {
        var query = db.Users.AsNoTracking().Include(u => u.Location).AsQueryable();
        if (canSignIn is { } flag)
            query = query.Where(u => u.CanSignIn == flag);
        var users = (await query.OrderBy(u => u.Name).ToListAsync())
            .Select(Mapping.ToDto).ToList();

        var assetCounts = await db.Assets.AsNoTracking()
            .Where(a => a.AssignedUserId != null)
            .GroupBy(a => a.AssignedUserId!.Value)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync();
        var assetsByUser = assetCounts.ToDictionary(x => x.Id, x => x.N);

        var stock = await db.StockMovements.AsNoTracking()
            .Where(m => m.UserId != null &&
                        (m.MovementType == StockMovementType.Issue || m.MovementType == StockMovementType.Return))
            .GroupBy(m => m.UserId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                Qty = g.Sum(m => m.MovementType == StockMovementType.Issue ? m.Quantity : -m.Quantity)
            })
            .ToListAsync();
        var stockByUser = stock.ToDictionary(x => x.Id, x => x.Qty);

        foreach (var u in users)
        {
            u.AssetCount = assetsByUser.GetValueOrDefault(u.Id);
            u.StockWithUser = Math.Max(0, stockByUser.GetValueOrDefault(u.Id));
        }
        return users;
    }

    public async Task<List<UnlinkedAssignmentDto>> UnlinkedAssignmentsAsync()
    {
        var names = await db.Assets.AsNoTracking()
            .Where(a => a.AssignedUserId == null
                        && a.AssignedUserName != null
                        && a.AssignedUserName != "")
            .GroupBy(a => a.AssignedUserName!)
            .Select(g => new UnlinkedAssignmentDto { Name = g.Key, AssetCount = g.Count() })
            .OrderByDescending(x => x.AssetCount)
            .ThenBy(x => x.Name)
            .ToListAsync();
        return names;
    }

    public async Task<List<LocationDto>> LocationsAsync() =>
        (await db.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync())
        .Select(Mapping.ToDto).ToList();

    public async Task<List<CategoryDto>> CategoriesAsync() =>
        (await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync())
        .Select(Mapping.ToDto).ToList();

    public Task<UserDto> CreateUserAsync(SaveUserRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            ValidateUser(req);
            var uname = req.Username.Trim();
            if (await db.Users.AnyAsync(u => u.Username.ToLower() == uname.ToLower()))
                throw new AppException(400, "validation", $"Username '{uname}' is already registered.");

            if (string.IsNullOrWhiteSpace(req.Password))
                throw new AppException(400, "validation", "Password is required.");
            var pwdError = AuthRules.ValidateNewPassword(req.Password);
            if (pwdError is not null)
                throw new AppException(400, "validation", pwdError);

            if (req.LocationId is { } loc && !await db.Locations.AnyAsync(l => l.Id == loc))
                throw new AppException(400, "validation", "Location is not valid.");

            var user = new User
            {
                EmployeeId = Mapping.Clean(req.EmployeeId),
                Username = uname,
                PasswordHash = PasswordHasher.Hash(req.Password),
                Name = req.Name.Trim(),
                Email = Mapping.Clean(req.Email),
                Department = Mapping.Clean(req.Department),
                LocationId = req.LocationId,
                Role = req.Role,
                IsActive = req.IsActive,
                CanSignIn = true
            };
            db.Users.Add(user);
            await SqliteGuard.SaveChangesAsync(db);
            return Mapping.ToDto(await db.Users.Include(u => u.Location).FirstAsync(u => u.Id == user.Id));
        });

    public Task<UserDto> CreatePersonAsync(SavePersonRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            var name = Mapping.Clean(req.Name) ?? throw new AppException(400, "validation", "Name is required.");
            if (await db.Users.AnyAsync(u => u.Name.ToLower() == name.ToLower()))
                throw new AppException(400, "validation", $"'{name}' is already in PAV. Use that record to assign assets.");

            var user = new User
            {
                Name = name,
                EmployeeId = Mapping.Clean(req.EmployeeId),
                Email = Mapping.Clean(req.Email),
                Department = Mapping.Clean(req.Department),
                Username = await NextPersonUsernameAsync(name),
                PasswordHash = "!",
                Role = UserRole.Guest,
                IsActive = req.IsActive,
                CanSignIn = false
            };
            db.Users.Add(user);
            await SqliteGuard.SaveChangesAsync(db);

            await db.Assets
                .Where(a => a.AssignedUserId == null && a.AssignedUserName != null && a.AssignedUserName.ToLower() == name.ToLower())
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.AssignedUserId, user.Id));

            return Mapping.ToDto(user);
        });

    public Task<UserDto> UpdatePersonAsync(int id, SavePersonRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            var name = Mapping.Clean(req.Name) ?? throw new AppException(400, "validation", "Name is required.");
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id)
                ?? throw new AppException(404, "not_found", "Person not found.");
            if (user.CanSignIn)
                throw new AppException(400, "validation", "That record is a sign-in account. Edit it in Settings.");
            if (await db.Users.AnyAsync(u => u.Id != id && u.Name.ToLower() == name.ToLower()))
                throw new AppException(400, "validation", $"'{name}' is already in PAV.");

            user.Name = name;
            user.EmployeeId = Mapping.Clean(req.EmployeeId);
            user.Email = Mapping.Clean(req.Email);
            user.Department = Mapping.Clean(req.Department);
            user.IsActive = req.IsActive;
            await SqliteGuard.SaveChangesAsync(db);
            return Mapping.ToDto(user);
        });

    public Task DeletePersonAsync(int id) =>
        writeLock.WriteAsync(async () =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id)
                ?? throw new AppException(404, "not_found", "Person not found.");
            if (user.CanSignIn)
                throw new AppException(400, "validation", "Sign-in accounts are managed in Settings.");
            if (await db.Assets.AnyAsync(a => a.AssignedUserId == id))
                throw new AppException(400, "validation", "Unassign their assets first.");
            db.Users.Remove(user);
            await SqliteGuard.SaveChangesAsync(db);
        });

    public Task<ImportPeopleResult> ImportPeopleFromInventoryAsync() =>
        writeLock.WriteAsync(async () =>
        {
            var result = new ImportPeopleResult();
            var users = await db.Users.Select(u => new { u.Id, u.Name, u.Username }).ToListAsync();
            var tuples = users.Select(u => (u.Id, u.Name, u.Username)).ToList();
            var takenUsernames = new HashSet<string>(users.Select(u => u.Username), StringComparer.OrdinalIgnoreCase);

            var assetNames = await db.Assets.AsNoTracking()
                .Where(a => a.AssignedUserName != null && a.AssignedUserName != "")
                .Select(a => a.AssignedUserName!)
                .ToListAsync();
            var stockNames = await db.StockMovements.AsNoTracking()
                .Where(m => m.AssignedUserName != null && m.AssignedUserName != "")
                .Select(m => m.AssignedUserName!)
                .ToListAsync();

            var groups = assetNames.Concat(stockNames)
                .Select(Mapping.Clean)
                .Where(n => n is not null)
                .GroupBy(n => n!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var name = group.OrderByDescending(n => n!.Length).First()!;
                if (IsJunkName(name))
                {
                    result.SkippedJunk++;
                    continue;
                }

                var matches = UserNameResolver.MatchIds(tuples, name);
                int id;
                if (matches.Count > 1)
                {
                    result.SkippedAmbiguous++;
                    continue;
                }
                if (matches.Count == 1)
                {
                    id = matches[0];
                    result.LinkedExisting++;
                }
                else
                {
                    var person = new User
                    {
                        Name = name,
                        Username = NextPersonUsername(name, takenUsernames),
                        PasswordHash = "!",
                        Role = UserRole.Guest,
                        IsActive = true,
                        CanSignIn = false
                    };
                    db.Users.Add(person);
                    await SqliteGuard.SaveChangesAsync(db);
                    id = person.Id;
                    tuples.Add((person.Id, person.Name, person.Username));
                    result.Created++;
                }

                result.LinkedAssets += await db.Assets
                    .Where(a => a.AssignedUserId == null
                                && a.AssignedUserName != null
                                && a.AssignedUserName.ToLower() == name.ToLower())
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.AssignedUserId, id));
                result.LinkedStock += await db.StockMovements
                    .Where(m => m.UserId == null
                                && m.AssignedUserName != null
                                && m.AssignedUserName.ToLower() == name.ToLower())
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, id));
            }

            return result;
        });

    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "-", "--", ".", "na", "n/a", "none", "nil", "unassigned", "not assigned"
    };

    private static bool IsJunkName(string name)
    {
        var n = name.Trim();
        return n.Length < 2 || JunkNames.Contains(n);
    }

    private static string NextPersonUsername(string name, HashSet<string> taken)
    {
        var slug = new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).Take(32).ToArray());
        if (string.IsNullOrWhiteSpace(slug)) slug = "person";
        var baseName = "person:" + slug;
        var candidate = baseName;
        var n = 2;
        while (taken.Contains(candidate))
        {
            candidate = baseName + "-" + n;
            n++;
        }
        taken.Add(candidate);
        return candidate;
    }

    private async Task<string> NextPersonUsernameAsync(string name)
    {
        var taken = new HashSet<string>(
            await db.Users.Select(u => u.Username).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        return NextPersonUsername(name, taken);
    }

    public Task<UserDto> UpdateUserAsync(int id, SaveUserRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            ValidateUser(req);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id)
                ?? throw new AppException(404, "not_found", "User not found.");
            if (!user.CanSignIn)
                throw new AppException(400, "validation", "That record is a directory person. Edit it on the Users tab.");

            var uname = req.Username.Trim();
            if (await db.Users.AnyAsync(u => u.Id != id && u.Username.ToLower() == uname.ToLower()))
                throw new AppException(400, "validation", $"Username '{uname}' is already registered.");

            if (user.Role == UserRole.Administrator && req.Role != UserRole.Administrator)
            {
                var otherAdmins = await db.Users.CountAsync(u => u.Id != id && u.Role == UserRole.Administrator && u.IsActive);
                if (otherAdmins == 0)
                    throw new AppException(400, "validation", "At least one active administrator is required.");
            }

            if (user.IsActive && !req.IsActive && user.Role == UserRole.Administrator)
            {
                var otherAdmins = await db.Users.CountAsync(u => u.Id != id && u.Role == UserRole.Administrator && u.IsActive);
                if (otherAdmins == 0)
                    throw new AppException(400, "validation", "At least one active administrator is required.");
            }

            user.EmployeeId = Mapping.Clean(req.EmployeeId);
            user.Username = uname;
            if (!string.IsNullOrWhiteSpace(req.Password))
            {
                var pwdError = AuthRules.ValidateNewPassword(req.Password);
                if (pwdError is not null)
                    throw new AppException(400, "validation", pwdError);
                user.PasswordHash = PasswordHasher.Hash(req.Password);
                user.MustChangePassword = false;
            }
            user.Name = req.Name.Trim();
            user.Email = Mapping.Clean(req.Email);
            user.Department = Mapping.Clean(req.Department);
            user.LocationId = req.LocationId;
            user.Role = req.Role;
            user.IsActive = req.IsActive;
            await SqliteGuard.SaveChangesAsync(db);
            return Mapping.ToDto(await db.Users.Include(u => u.Location).FirstAsync(u => u.Id == user.Id));
        });

    public Task<LocationDto> CreateLocationAsync(SaveLocationRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                throw new AppException(400, "validation", "Location name is required.");
            var name = req.Name.Trim();
            if (await db.Locations.AnyAsync(l => l.Name.ToLower() == name.ToLower()))
                throw new AppException(400, "validation", $"Location '{name}' already exists.");
            var loc = new Location { Name = name, Description = Mapping.Clean(req.Description) };
            db.Locations.Add(loc);
            await SqliteGuard.SaveChangesAsync(db);
            return Mapping.ToDto(loc);
        });

    public Task<LocationDto> UpdateLocationAsync(int id, SaveLocationRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                throw new AppException(400, "validation", "Location name is required.");
            var loc = await db.Locations.FirstOrDefaultAsync(l => l.Id == id)
                ?? throw new AppException(404, "not_found", "Location not found.");
            var name = req.Name.Trim();
            if (await db.Locations.AnyAsync(l => l.Id != id && l.Name.ToLower() == name.ToLower()))
                throw new AppException(400, "validation", $"Location '{name}' already exists.");
            loc.Name = name;
            loc.Description = Mapping.Clean(req.Description);
            await SqliteGuard.SaveChangesAsync(db);
            return Mapping.ToDto(loc);
        });

    public Task DeleteLocationAsync(int id) =>
        writeLock.WriteAsync(async () =>
        {
            var loc = await db.Locations.FirstOrDefaultAsync(l => l.Id == id)
                ?? throw new AppException(404, "not_found", "Location not found.");
            var inUse = await db.Assets.AnyAsync(a => a.LocationId == id);
            if (inUse)
                throw new AppException(400, "validation", "Location is in use by one or more assets.");
            db.Locations.Remove(loc);
            await SqliteGuard.SaveChangesAsync(db);
        });

    public Task<CategoryDto> CreateCategoryAsync(SaveCategoryRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                throw new AppException(400, "validation", "Category name is required.");
            var name = req.Name.Trim();
            if (await db.Categories.AnyAsync(c => c.Name.ToLower() == name.ToLower()))
                throw new AppException(400, "validation", $"Category '{name}' already exists.");
            var cat = new Category { Name = name, Description = Mapping.Clean(req.Description) };
            db.Categories.Add(cat);
            await SqliteGuard.SaveChangesAsync(db);
            return Mapping.ToDto(cat);
        });

    public Task<CategoryDto> UpdateCategoryAsync(int id, SaveCategoryRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                throw new AppException(400, "validation", "Category name is required.");
            var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new AppException(404, "not_found", "Category not found.");
            var name = req.Name.Trim();
            if (await db.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower()))
                throw new AppException(400, "validation", $"Category '{name}' already exists.");
            cat.Name = name;
            cat.Description = Mapping.Clean(req.Description);
            await SqliteGuard.SaveChangesAsync(db);
            return Mapping.ToDto(cat);
        });

    public Task DeleteCategoryAsync(int id) =>
        writeLock.WriteAsync(async () =>
        {
            var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new AppException(404, "not_found", "Category not found.");
            var inUse = await db.Assets.AnyAsync(a => a.CategoryId == id);
            if (inUse)
                throw new AppException(400, "validation", "Category is in use by one or more assets.");
            db.Categories.Remove(cat);
            await SqliteGuard.SaveChangesAsync(db);
        });

    private static void ValidateUser(SaveUserRequest req)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(req.Username))
            errors.Add("Username is required.");
        if (string.IsNullOrWhiteSpace(req.Name))
            errors.Add("Name is required.");
        if (errors.Count > 0)
            throw new AppException(400, "validation", "Please correct the highlighted fields.", errors);
    }
}
