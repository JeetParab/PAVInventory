using Microsoft.EntityFrameworkCore;
using PAV.Core.Services;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Core.Data;

public static class SeedData
{
    public static async Task EnsureSeededAsync(AppDbContext db)
    {
        try
        {
            if (!await db.Categories.AnyAsync())
            {
                db.Categories.AddRange(
                    new Category { Name = "Laptop", Description = "Notebook computers", Family = CategoryFamily.Computer },
                    new Category { Name = "Desktop", Description = "Desktop PCs", Family = CategoryFamily.Computer },
                    new Category { Name = "All-in-One", Description = "All-in-one PCs", Family = CategoryFamily.Computer },
                    new Category { Name = "Network", Description = "Switches, APs, routers", Family = CategoryFamily.Computer },
                    new Category { Name = "Monitor", Description = "Displays", Family = CategoryFamily.Peripheral },
                    new Category { Name = "Printer", Description = "Printers and MFDs", Family = CategoryFamily.Peripheral },
                    new Category { Name = "UPS", Description = "Uninterruptible power supplies", Family = CategoryFamily.Peripheral },
                    new Category { Name = "Accessory", Description = "Peripherals and accessories", Family = CategoryFamily.Peripheral },
                    new Category { Name = "Other", Description = "Uncategorised assets", Family = CategoryFamily.Peripheral });
            }

            if (!await db.Locations.AnyAsync())
            {
                db.Locations.AddRange(
                    new Location { Name = "Mumbai Office", Description = "BKC / main Mumbai office" },
                    new Location { Name = "Mumbai Office - VIPS", Description = "VIP / CMD / DMD floor" },
                    new Location { Name = "Mumbai Controls", Description = "Controls / EIT" },
                    new Location { Name = "Mumbai SVCL", Description = "SVCL" });
            }

            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteGuard.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
        }
    }
}
