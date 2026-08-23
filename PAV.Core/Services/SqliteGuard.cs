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
            if (e.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
