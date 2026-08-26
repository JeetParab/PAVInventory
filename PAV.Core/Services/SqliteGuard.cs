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
}
