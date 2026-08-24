using Microsoft.EntityFrameworkCore;
using PAV.Shared.Models;

namespace PAV.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<AssetHistory> AssetHistory => Set<AssetHistory>();
    public DbSet<IpRange> IpRanges => Set<IpRange>();
    public DbSet<IpRecord> IpRecords => Set<IpRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Asset>(e =>
        {
            e.ToTable("Assets");
            e.HasKey(x => x.Id);
            e.Property(x => x.AssetTag).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.AssetTag).IsUnique();
            e.Property(x => x.SerialNumber).HasMaxLength(128);
            e.HasIndex(x => x.SerialNumber)
                .IsUnique()
                .HasFilter("SerialNumber IS NOT NULL AND SerialNumber != ''");
            e.Property(x => x.Manufacturer).HasMaxLength(128);
            e.Property(x => x.Model).HasMaxLength(256);
            e.Property(x => x.Hostname).HasMaxLength(128);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.AssignedUserName).HasMaxLength(256);
            e.Property(x => x.Designation).HasMaxLength(256);
            e.Property(x => x.AlternateUser).HasMaxLength(256);
            e.Property(x => x.Domain).HasMaxLength(64);
            e.Property(x => x.MacAddress).HasMaxLength(64);
            e.Property(x => x.Processor).HasMaxLength(256);
            e.Property(x => x.Ram).HasMaxLength(64);
            e.Property(x => x.Storage).HasMaxLength(64);
            e.Property(x => x.OperatingSystem).HasMaxLength(128);
            e.Property(x => x.DcInstalled).HasMaxLength(16);
            e.Property(x => x.AvInstalled).HasMaxLength(16);
            e.Property(x => x.MsOfficeVersion).HasMaxLength(64);
            e.Property(x => x.MfaEnabled).HasMaxLength(16);
            e.Property(x => x.IvantiInstalled).HasMaxLength(16);
            e.Property(x => x.AdminRights).HasMaxLength(16);
            e.Property(x => x.UsbAccess).HasMaxLength(16);
            e.Property(x => x.ChromeUpdated).HasMaxLength(16);
            e.Property(x => x.StockAvailability).HasMaxLength(32);
            e.Property(x => x.StockWorking).HasMaxLength(32);
            e.Property(x => x.PmCompleted).HasMaxLength(16);
            e.Property(x => x.CollectBy).HasMaxLength(128);
            e.Property(x => x.Remarks).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.LocationId);
            e.HasIndex(x => x.Hostname);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.EmployeeId).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Department).HasMaxLength(128);
            e.Property(x => x.Role).HasConversion<int>();
            e.Property(x => x.MustChangePassword);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Location>(e =>
        {
            e.ToTable("Locations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(512);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("Categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(512);
        });

        modelBuilder.Entity<AssetHistory>(e =>
        {
            e.ToTable("AssetHistory");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.FieldName).HasMaxLength(64);
            e.Property(x => x.OldValue).HasMaxLength(512);
            e.Property(x => x.NewValue).HasMaxLength(512);
            e.Property(x => x.Action).HasConversion<int>();
            e.HasIndex(x => x.AssetId);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<IpRange>(e =>
        {
            e.ToTable("IpRanges");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Cidr).HasMaxLength(32).IsRequired();
            e.Property(x => x.GatewayIp).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(512);
            e.HasIndex(x => x.FloorNumber).IsUnique();
            e.HasIndex(x => x.Cidr).IsUnique();
        });

        modelBuilder.Entity<IpRecord>(e =>
        {
            e.ToTable("IpRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Address).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Address).IsUnique();
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.AssignedDevice).HasMaxLength(256);
            e.Property(x => x.AssignedUser).HasMaxLength(256);
            e.Property(x => x.Department).HasMaxLength(128);
            e.Property(x => x.MacAddress).HasMaxLength(64);
            e.Property(x => x.DeviceType).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.AllocatedBy).HasMaxLength(128);
            e.HasOne(x => x.Range).WithMany(r => r.Addresses).HasForeignKey(x => x.RangeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.RangeId);
            e.HasIndex(x => x.Status);
        });
    }
}
