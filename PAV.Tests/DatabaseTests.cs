using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;
using PAV.Core.Services;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Tests;

/// <summary>Runs the real services against a throwaway SQLite database.</summary>
public sealed class DatabaseTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pav-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteWriteLock _lock = new();
    private PavDatabase _pav = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        _pav = new PavDatabase(_folder);
        await _pav.OpenAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp folder */ }
        return Task.CompletedTask;
    }

    private static User Person(string name, string username, string? sam) => new()
    {
        Name = name,
        Username = username,
        PasswordHash = "!",
        Role = UserRole.Guest,
        CanSignIn = false,
        SamAccount = sam
    };

    [Fact]
    public async Task Renaming_an_AD_user_onto_another_persons_user_id_is_refused()
    {
        int adId;
        await using (var db = _pav.Create())
        {
            db.Users.AddRange(Person("Alpha", "alpha", "jdoe"), Person("Beta", "beta", "jdoe2"));
            var ad = new AdDirectoryEntry { Sam = "jdoe", Name = "Alpha" };
            db.AdDirectory.Add(ad);
            await db.SaveChangesAsync();
            adId = ad.Id;
        }

        await using var work = _pav.Create();
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            new AdDirectoryService(work, _lock).SaveAsync(adId, new SaveAdUserRequest { Sam = "jdoe2", Name = "Alpha" }));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("already assigned", ex.Message);
    }

    [Fact]
    public async Task Renaming_an_AD_user_to_a_free_user_id_updates_the_linked_person()
    {
        int adId;
        await using (var db = _pav.Create())
        {
            db.Users.Add(Person("Alpha", "alpha", "jdoe"));
            var ad = new AdDirectoryEntry { Sam = "jdoe", Name = "Alpha" };
            db.AdDirectory.Add(ad);
            await db.SaveChangesAsync();
            adId = ad.Id;
        }

        await using (var work = _pav.Create())
            await new AdDirectoryService(work, _lock).SaveAsync(adId, new SaveAdUserRequest { Sam = "jdoe3", Name = "Alpha" });

        await using var check = _pav.Create();
        var person = await check.Users.AsNoTracking().SingleAsync(u => u.Username == "alpha");
        Assert.Equal("jdoe3", person.SamAccount);
    }

    [Fact]
    public async Task Changing_an_assets_category_and_location_saves_cleanly()
    {
        var admin = new CurrentUser { User = new User { Name = "Test Admin", Username = "admin", Role = UserRole.Administrator } };
        List<int> computerCats, locs;
        await using (var db = _pav.Create())
        {
            computerCats = await db.Categories.Where(c => c.Family == CategoryFamily.Computer).OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
            locs = await db.Locations.OrderBy(l => l.Id).Select(l => l.Id).ToListAsync();
        }

        AssetDetailDto created;
        await using (var db = _pav.Create())
            created = await new AssetService(db, _lock).CreateAsync(new SaveAssetRequest
            {
                AssetTag = "TEST-001",
                CategoryId = computerCats[0],
                LocationId = locs[0],
                Status = AssetStatus.InStock
            }, admin);

        await using (var db = _pav.Create())
        {
            var save = new SaveAssetRequest
            {
                AssetTag = created.AssetTag,
                CategoryId = computerCats[1],
                LocationId = locs[1],
                Status = AssetStatus.InUse,
                Version = created.Version
            };
            var updated = await new AssetService(db, _lock).UpdateAsync(created.Id, save, admin);
            Assert.Equal(computerCats[1], updated.CategoryId);
            Assert.Equal(locs[1], updated.LocationId);
            Assert.Equal(created.Version + 1, updated.Version);
        }
    }
}
