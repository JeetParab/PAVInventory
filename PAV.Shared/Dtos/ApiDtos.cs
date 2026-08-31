using PAV.Shared.Enums;

namespace PAV.Shared.Dtos;

public class ApiError
{
    public string Code { get; set; } = "error";
    public string Message { get; set; } = "";
    public List<string>? Errors { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginResponse
{
    public string Token { get; set; } = "";
    public MeDto User { get; set; } = new();
}

public class SetupAdministratorRequest
{
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class AssetListDto
{
    public int Id { get; set; }
    public string AssetTag { get; set; } = "";
    public int? SrNo { get; set; }
    public int CategoryId { get; set; }
    public string Category { get; set; } = "";
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string MakeModel => string.Join(" ", new[] { Manufacturer, Model }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string DupLabel => $"{Hostname}  {IpAddress}  {AssignedUser}  Sr {SrNo}";
    public string? SerialNumber { get; set; }
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public int? LocationId { get; set; }
    public string? Location { get; set; }
    public int? AssignedUserId { get; set; }
    public string? AssignedUser { get; set; }
    public string? Designation { get; set; }
    public string? AlternateUser { get; set; }
    public string Status { get; set; } = "";
    public AssetStatus StatusValue { get; set; }
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
    public string? Remarks { get; set; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Remarks);
    public DateTime? PurchaseDate { get; set; }
    public DateTime? WarrantyExpiry { get; set; }
    public int Version { get; set; }

    public AssetListDto Clone() => (AssetListDto)MemberwiseClone();
}

public class AssetDetailDto : AssetListDto
{
    public string? AssignedDepartment { get; set; }
    public DateTime? AssignedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<HistoryDto> History { get; set; } = [];
}

public class SaveAssetRequest
{
    public string AssetTag { get; set; } = "";
    public int? SrNo { get; set; }
    public int CategoryId { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public int? LocationId { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.InStock;
    public int? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public string? Designation { get; set; }
    public string? AlternateUser { get; set; }
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
    public int Version { get; set; }
}

public class BulkEditRequest
{
    public List<int> Ids { get; set; } = [];
    public AssetStatus? Status { get; set; }
    public int? LocationId { get; set; }
    public bool SetLocation { get; set; }
    public bool SetAssignedUser { get; set; }
    public int? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public string? Designation { get; set; }
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public string? Domain { get; set; }
    public string? MfaEnabled { get; set; }
    public string? AdminRights { get; set; }
    public string? UsbAccess { get; set; }
    public DateTime? LastConnected { get; set; }
    public bool SetLastConnected { get; set; }
    public string? CollectBy { get; set; }
}

public class BulkAddRequest
{
    public int CategoryId { get; set; }
    public int? LocationId { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.InStock;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Designation { get; set; }
    public string? Domain { get; set; }
    public string Lines { get; set; } = "";
}

public class DuplicateGroupDto
{
    public string SerialNumber { get; set; } = "";
    public List<AssetListDto> Assets { get; set; } = [];
}

public class HistoryDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public HistoryAction Action { get; set; }
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string? EmployeeId { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Department { get; set; }
    public int? LocationId { get; set; }
    public string? Location { get; set; }
    public UserRole Role { get; set; }
    public string RoleName { get; set; } = "";
    public bool IsActive { get; set; }
    public int AssetCount { get; set; }
    public int StockWithUser { get; set; }

    public override string ToString() => Name;
}

public class UnlinkedAssignmentDto
{
    public string Name { get; set; } = "";
    public int AssetCount { get; set; }
}

public class SaveUserRequest
{
    public string? EmployeeId { get; set; }
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Department { get; set; }
    public int? LocationId { get; set; }
    public UserRole Role { get; set; } = UserRole.Engineer;
    public bool IsActive { get; set; } = true;
}

public class LocationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public override string ToString() => Name;
}

public class SaveLocationRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public override string ToString() => Name;
}

public class SaveCategoryRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class MeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public UserRole Role { get; set; }
    public string RoleName { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public bool MustChangePassword { get; set; }
}

public class HealthDto
{
    public string Status { get; set; } = "ok";
    public DateTime ServerTime { get; set; }
    public int AssetCount { get; set; }
}

public class InventoryLoadDto
{
    public List<AssetListDto> Assets { get; set; } = [];
    public List<CategoryDto> Categories { get; set; } = [];
    public List<LocationDto> Locations { get; set; } = [];
    public List<UserDto> Users { get; set; } = [];
}

public class DashboardDto
{
    public int TotalAssets { get; set; }
    public int InUse { get; set; }
    public int InStock { get; set; }
    public int UnderRepair { get; set; }
    public int Damaged { get; set; }
    public int Standby { get; set; }
    public List<CategoryCountDto> ByCategory { get; set; } = [];
    public List<HistoryDto> RecentActivity { get; set; } = [];
    public List<AssetListDto> WarrantyExpiringSoon { get; set; } = [];
}

public class CategoryCountDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class ImportColumnMap
{
    public string AssetTag { get; set; } = "Asset ID";
    public string? Category { get; set; } = "Category";
    public string? Manufacturer { get; set; } = "Manufacturer";
    public string? Model { get; set; } = "Model";
    public string? SerialNumber { get; set; } = "Serial Number";
    public string? Hostname { get; set; } = "Hostname";
    public string? Location { get; set; } = "Location";
    public string? Status { get; set; } = "Status";
    public string? AssignedUser { get; set; } = "Assigned User";
    public string? PurchaseDate { get; set; } = "Purchase Date";
    public string? WarrantyExpiry { get; set; } = "Warranty Expiry";
    public string? Remarks { get; set; } = "Remarks";
}

public class ImportPreviewRow
{
    public int RowNumber { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new();
    public List<string> Errors { get; set; } = [];
    public bool IsValid => Errors.Count == 0;
    public string ErrorText => string.Join("; ", Errors);
}

public class ImportPreviewResponse
{
    public List<string> Columns { get; set; } = [];
    public List<ImportPreviewRow> Rows { get; set; } = [];
    public int ValidCount { get; set; }
    public int ErrorCount { get; set; }
    public int DuplicateCount { get; set; }
    public int MissingIdentityCount { get; set; }
    public int InvalidStatusCount { get; set; }
    public string SummaryText { get; set; } = "";
}

public class ImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = [];
}

public class BackupInfo
{
    public string FileName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
}

public class RestoreRequest
{
    public string FileName { get; set; } = "";
}

public class IpRangeDto
{
    public int Id { get; set; }
    public int FloorNumber { get; set; }
    public string Name { get; set; } = "";
    public int ThirdOctet { get; set; }
    public string Cidr { get; set; } = "";
    public string? GatewayIp { get; set; }
    public string? Notes { get; set; }

    public override string ToString() => Name;
}

public class IpFloorSummaryDto
{
    public int RangeId { get; set; }
    public int FloorNumber { get; set; }
    public string Name { get; set; } = "";
    public string Cidr { get; set; } = "";
    public int Total { get; set; }
    public int Used { get; set; }
    public int Free { get; set; }
    public int Reserved { get; set; }
    public string? NextFreeIp { get; set; }
    public string UtilLabel { get; set; } = "";
}

public class IpOverviewDto
{
    public int Total { get; set; }
    public int Used { get; set; }
    public int Free { get; set; }
    public int Reserved { get; set; }
    public List<IpFloorSummaryDto> Floors { get; set; } = [];
}

public class IpAddressDto
{
    public int Id { get; set; }
    public int RangeId { get; set; }
    public int FloorNumber { get; set; }
    public string FloorName { get; set; } = "";
    public string Address { get; set; } = "";
    public int HostOctet { get; set; }
    public string Subnet { get; set; } = "";
    public IpStatus StatusValue { get; set; }
    public string Status { get; set; } = "";
    public string? AssignedDevice { get; set; }
    public string? AssignedUser { get; set; }
    public string? Department { get; set; }
    public DateTime? DateAssigned { get; set; }
    public string? MacAddress { get; set; }
    public string? DeviceType { get; set; }
    public string? Notes { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string? AllocatedBy { get; set; }
}

public class IpCheckResultDto
{
    public bool InPool { get; set; }
    public string Address { get; set; } = "";
    public string Message { get; set; } = "";
    public IpAddressDto? Record { get; set; }
}

public class AssignIpRequest
{
    public int? Id { get; set; }
    public int? RangeId { get; set; }
    public string? Address { get; set; }
    public string? AssignedDevice { get; set; }
    public string? AssignedUser { get; set; }
    public string? Department { get; set; }
    public string? MacAddress { get; set; }
    public string? DeviceType { get; set; }
    public string? Notes { get; set; }
}

public class IpImportResult
{
    public int Ranges { get; set; }
    public int Addresses { get; set; }
    public int Updated { get; set; }
    public List<string> Errors { get; set; } = [];
    public string Summary { get; set; } = "";
}

public class StockItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string Unit { get; set; } = "pcs";
    public int OnHand { get; set; }
    public int MinimumQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NeedsReview { get; set; }
    public string? Notes { get; set; }
    public string StockStatus { get; set; } = "In Stock";
    public bool IsLow { get; set; }
    public int Opening { get; set; }
    public int Received { get; set; }
    public int Issued { get; set; }
    public int Returned { get; set; }
    public int Adjusted { get; set; }
    public DateTime UpdatedAt { get; set; }

    public override string ToString() => Name;
}

public class StockMovementDto
{
    public int Id { get; set; }
    public int StockItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string MovementType { get; set; } = "";
    public int Quantity { get; set; }
    public int SignedQuantity { get; set; }
    public int? UserId { get; set; }
    public string? AssignedUser { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public string? SerialNumber { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class SaveStockItemRequest
{
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Unit { get; set; }
    public int MinimumQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public class StockMoveRequest
{
    public int StockItemId { get; set; }
    public int Quantity { get; set; }
    public int? UserId { get; set; }
    public string? AssignedUserName { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public string? SerialNumber { get; set; }
}

public class StockOverviewDto
{
    public int ItemCount { get; set; }
    public int OnHandUnits { get; set; }
    public int LowStock { get; set; }
    public int OutOfStock { get; set; }
    public List<StockItemDto> Items { get; set; } = [];
}

public class StockImportLineDto
{
    public string Name { get; set; } = "";
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string Category { get; set; } = "";
    public int OpeningQty { get; set; }
    public int IssuedQty { get; set; }
    public string Classification { get; set; } = "Stock";
    public string Action { get; set; } = "Create";
    public string? Notes { get; set; }
    public List<string> Issues { get; set; } = [];
}

public class StockImportPreviewDto
{
    public string SourceName { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public bool CanImport { get; set; }
    public int NewItems { get; set; }
    public int ExistingMatched { get; set; }
    public int OpeningUnits { get; set; }
    public int IssueRows { get; set; }
    public int AmbiguousItems { get; set; }
    public int ReviewRows { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Issues { get; set; } = [];
    public List<string> SkippedSheets { get; set; } = [];
    public List<StockImportLineDto> Lines { get; set; } = [];
}

public class StockImportResultDto
{
    public int ItemsCreated { get; set; }
    public int OpeningUnits { get; set; }
    public int IssuesRecorded { get; set; }
    public int SkippedExisting { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Errors { get; set; } = [];
}

public class StockIntegrityRowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int StoredOnHand { get; set; }
    public int CalculatedOnHand { get; set; }
    public string Status { get; set; } = "PASS";
}

public class StockIntegrityDto
{
    public int ItemCount { get; set; }
    public int PassCount { get; set; }
    public int MismatchCount { get; set; }
    public string Summary { get; set; } = "";
    public List<StockIntegrityRowDto> Rows { get; set; } = [];
}

