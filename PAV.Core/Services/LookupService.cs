using Microsoft.EntityFrameworkCore;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;
using PAV.Core.Data;

namespace PAV.Core.Services;

public class LookupService(AppDbContext db, IWriteLock writeLock)

{
    public async Task<List<UserDto>> UsersAsync() =>
        (await db.Users.AsNoTracking().Include(u => u.Location).OrderBy(u => u.Name).ToListAsync())
        .Select(Mapping.ToDto).ToList();

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
                IsActive = req.IsActive
            };
            db.Users.Add(user);
            await SqliteGuard.SaveChangesAsync(db);
            return Mapping.ToDto(await db.Users.Include(u => u.Location).FirstAsync(u => u.Id == user.Id));
        });

    public Task<UserDto> UpdateUserAsync(int id, SaveUserRequest req) =>
        writeLock.WriteAsync(async () =>
        {
            ValidateUser(req);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id)
                ?? throw new AppException(404, "not_found", "User not found.");

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
