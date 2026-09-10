using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;

namespace PAV.Core.Services;

public static class SqliteGuard
{
    public static async Task SaveChangesAsync(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AppException(409, "conflict",
                "This record was modified by another user. Refresh and try again.");
        }
        catch (DbUpdateException ex)
        {
            throw new AppException(400, "validation", Describe(ex));
        }
    }

    public static string Describe(Exception ex)
    {
        if (IsBusy(ex))
            return "The shared database is busy (another PC is writing). Wait a second and press Save again.";

        var text = Flatten(ex);

        if (IsUniqueViolation(ex))
        {
            if (Mentions(text, "SamAccount", "IX_Users_SamAccount"))
                return "That user ID is already on another PAV user.";
            if (Mentions(text, "IX_AdDirectory_Sam", "AdDirectory.Sam"))
                return "That user ID is already in AD users.";
            if (Mentions(text, "Users.Username", "IX_Users_Username"))
                return "That username is already in use.";
            if (Mentions(text, "SerialNumber", "IX_Assets_SerialNumber"))
                return "Serial number already exists.";
            if (Mentions(text, "AssetTag", "IX_Assets_AssetTag"))
                return "Asset ID already exists.";
            if (Mentions(text, "IpRecords.Address", "IX_IpRecords_Address"))
                return "That IP address is already in IP Inventory.";
            if (Mentions(text, "Locations.Name", "IX_Locations_Name"))
                return "A location with that name already exists.";
            return "This save conflicts with an existing record (duplicate Asset ID, serial, user id, or similar).";
        }

        if (Mentions(text, "FOREIGN KEY", "constraint failed"))
            return "This save refers to a user, location or category that no longer exists. Refresh and try again.";

        var inner = Innermost(ex);
        if (!string.IsNullOrWhiteSpace(inner)
            && inner.IndexOf("See the inner exception", StringComparison.OrdinalIgnoreCase) < 0)
            return inner;

        return "Could not save. " + (text.Length > 0 ? text : ex.Message);
    }

    public static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqliteException sqlite)
            {
                if (sqlite.SqliteExtendedErrorCode is 2067 or 1555)
                    return true;
                if (sqlite.SqliteErrorCode == 19
                    && sqlite.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            if (e is SqlException sql && sql.Number is 2601 or 2627)
                return true;
            if (e.Message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsBusy(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqliteException s && s.SqliteErrorCode is 5 or 6)
                return true;
        }
        return false;
    }

    public static bool IsConnectFailure(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && sql.Number is 2 or 53 or -1 or 64 or 233 or 4060 or 18456 or 10054 or 10060 or 11001)
                return true;
            if (e is TimeoutException) return true;
            if (e is IOException) return true;
            var name = e.GetType().Name;
            if (name is "SqlException" or "Win32Exception") return true;
        }
        return false;
    }

    public static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message?.Trim();
            if (string.IsNullOrWhiteSpace(m)) continue;
            if (parts.Count > 0 && parts[^1] == m) continue;
            parts.Add(m);
        }
        return string.Join(" — ", parts);
    }

    private static string Innermost(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex.Message?.Trim() ?? "";
    }

    private static bool Mentions(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
}
