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
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new AppException(400, "validation",
                "Asset ID or serial number already exists.");
        }
    }

    public static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqliteException sqlite && sqlite.SqliteErrorCode == 19)
                return true;
            if (e is SqlException sql && sql.Number is 2601 or 2627)
                return true;
            if (e.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
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
}
