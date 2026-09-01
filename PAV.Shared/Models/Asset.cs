using PAV.Shared.Enums;

namespace PAV.Shared.Models;

public class Asset
{
    public int Id { get; set; }
    public string AssetTag { get; set; } = "";
    public int? SrNo { get; set; }
    public string? SerialNumber { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public int? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }
    public string? AssignedUserName { get; set; }
    public string? Designation { get; set; }
    public string? AlternateUser { get; set; }
    public DateTime? AssignedDate { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.InStock;
    public string? Domain { get; set; }
    public string? MacAddress { get; set; }
    public string? Processor { get; set; }
    public string? Ram { get; set; }
    public string? Storage { get; set; }
    public string? OperatingSystem { get; set; }
    public string? DcInstalled { get; set; }
    public string? AvInstalled { get; set; }
    public string? MsOfficeVersion { get; set; }
    public string? MfaEnabled { get; set; }
    public string? IvantiInstalled { get; set; }
    public string? AdminRights { get; set; }
    public string? UsbAccess { get; set; }
    public string? ChromeUpdated { get; set; }
    public string? StockAvailability { get; set; }
    public string? StockWorking { get; set; }
    public string? PmCompleted { get; set; }
    public DateTime? LastConnected { get; set; }
    public string? CollectBy { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? WarrantyExpiry { get; set; }
    public string? Remarks { get; set; }
    public bool IsTemporary { get; set; }
    public bool NeedsReview { get; set; }
    public string? MeLogon { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
