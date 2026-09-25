using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAV.Core.Services;
using PAV.Shared.Enums;

namespace PAV.Tests;

public class AdLogonTests
{
    [Theory]
    [InlineData(@"CORP\JDoe", "jdoe")]
    [InlineData("jdoe@sidbi.in", "jdoe")]
    [InlineData(@"Administrator, CORP\jdoe", "jdoe")]
    [InlineData("  jdoe  ", "jdoe")]
    public void Normalize_extracts_the_user_id(string raw, string expected) =>
        Assert.Equal(expected, AdLogon.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("Administrator")]
    public void Normalize_returns_null_for_non_users(string? raw) =>
        Assert.Null(AdLogon.Normalize(raw));
}

public class UserNameResolverTests
{
    private static readonly List<(int Id, string Name, string Username, string? Sam)> Users =
    [
        (1, "Jeet Parab", "jeet", "jparab"),
        (2, "Rahul Shah", "rahul", null),
        (3, "Rahul Shah", "rahul2", null)
    ];

    [Theory]
    [InlineData("jeet parab", 1)]
    [InlineData("JPARAB", 1)]
    [InlineData("jeet", 1)]
    [InlineData("Jeet Parab  (jparab)", 1)]
    public void ResolveUniqueId_matches_name_username_or_user_id(string typed, int expected) =>
        Assert.Equal(expected, UserNameResolver.ResolveUniqueId(Users, typed));

    [Theory]
    [InlineData("Rahul Shah")]
    [InlineData("nobody")]
    [InlineData("")]
    public void ResolveUniqueId_never_guesses_between_several_or_zero_matches(string typed) =>
        Assert.Null(UserNameResolver.ResolveUniqueId(Users, typed));

    [Theory]
    [InlineData("Jeet Parab", "jparab", "Jeet Parab  (jparab)")]
    [InlineData("Jeet Parab", "person:jeetparab", "Jeet Parab")]
    [InlineData("", "jparab", "jparab")]
    public void Display_shows_the_user_id_when_it_is_meaningful(string name, string sam, string expected) =>
        Assert.Equal(expected, UserNameResolver.Display(name, sam));
}

public class SqliteGuardTests
{
    private static Exception Wrapped(string message, int code, int extended) =>
        new DbUpdateException("An error occurred while saving the entity changes. See the inner exception for details.",
            new SqliteException(message, code, extended));

    [Theory]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: Users.SamAccount'.", "That user ID is already on another PAV user.")]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: Assets.SerialNumber'.", "Serial number already exists.")]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: Assets.AssetTag'.", "Asset ID already exists.")]
    public void Describe_names_the_duplicate_field(string sqliteMessage, string expected) =>
        Assert.Equal(expected, SqliteGuard.Describe(Wrapped(sqliteMessage, 19, 2067)));

    [Fact]
    public void Describe_explains_a_missing_foreign_key()
    {
        var text = SqliteGuard.Describe(Wrapped("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787));
        Assert.Contains("no longer exists", text);
    }

    [Fact]
    public void Describe_does_not_mislabel_not_null_as_a_foreign_key()
    {
        var text = SqliteGuard.Describe(Wrapped("SQLite Error 19: 'NOT NULL constraint failed: Assets.AssetTag'.", 19, 1299));
        Assert.DoesNotContain("no longer exists", text);
        Assert.Contains("NOT NULL", text);
    }

    [Fact]
    public void Describe_reports_a_busy_shared_database() =>
        Assert.Contains("busy", SqliteGuard.Describe(Wrapped("SQLite Error 5: 'database is locked'.", 5, 5)));
}

public class StockExcelTests
{
    [Theory]
    [InlineData("Dell Latitude laptop", "SerializedAsset")]
    [InlineData("HP LaserJet printer", "SerializedAsset")]
    [InlineData("Dell 24 inch monitor", "Ambiguous")]
    public void Classify_recognises_serialised_and_ambiguous_items(string name, string expected) =>
        Assert.Equal(expected, StockExcel.Classify(name));

    [Theory]
    [InlineData("Laptop charger")]
    [InlineData("Laptop cooler")]
    [InlineData("Label printer")]
    public void Classify_does_not_treat_accessories_as_serialised_assets(string name) =>
        Assert.NotEqual("SerializedAsset", StockExcel.Classify(name));

    [Fact]
    public void NormalizeKey_collapses_case_and_whitespace() =>
        Assert.Equal("hp toner hp", StockExcel.NormalizeKey("  HP   Toner ", "HP"));
}

public class AssetStatusTests
{
    [Fact]
    public void Every_status_round_trips_through_its_display_name()
    {
        foreach (var status in AssetStatusNames.All)
        {
            Assert.True(AssetStatusNames.TryParse(status.Display(), out var parsed), status.ToString());
            Assert.Equal(status, parsed);
        }
    }
}
