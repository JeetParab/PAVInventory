using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class AssetEditViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly int? _id;
    private readonly int _version;
    private readonly List<UserDto> _users;
    private bool _lockName;

    public string Title { get; }
    public List<CategoryDto> Categories { get; }
    public List<LocationDto> Locations { get; }
    public List<string> AssigneeChoices { get; }
    public List<string> Statuses { get; } = AssetStatusNames.All.Select(s => s.Display()).ToList();
    public List<string> YesNo { get; } = ["", "Yes", "No"];
    public List<string> OfficeChoices { get; }
    public List<string> CollectByChoices { get; }

    [ObservableProperty] private string assetTag = "";
    [ObservableProperty] private string? srNoText;
    [ObservableProperty] private int categoryId;
    [ObservableProperty] private string? manufacturer;
    [ObservableProperty] private string? model;
    [ObservableProperty] private string? serialNumber;
    [ObservableProperty] private string? hostname;
    [ObservableProperty] private string? ipAddress;
    [ObservableProperty] private int locationId;
    [ObservableProperty] private string status = AssetStatus.InStock.Display();
    [ObservableProperty] private int? assignedUserId;
    [ObservableProperty] private string? assignedUserName;
    [ObservableProperty] private string? designation;
    [ObservableProperty] private string? alternateUser;
    [ObservableProperty] private string? domain;
    [ObservableProperty] private string? macAddress;
    [ObservableProperty] private string? processor;
    [ObservableProperty] private string? ram;
    [ObservableProperty] private string? storage;
    [ObservableProperty] private string? operatingSystem;
    [ObservableProperty] private string? dcInstalled;
    [ObservableProperty] private string? avInstalled;
    [ObservableProperty] private string? msOfficeVersion;
    [ObservableProperty] private string? mfaEnabled;
    [ObservableProperty] private string? ivantiInstalled;
    [ObservableProperty] private string? adminRights;
    [ObservableProperty] private string? usbAccess;
    [ObservableProperty] private string? chromeUpdated;
    [ObservableProperty] private string? pmCompleted;
    [ObservableProperty] private DateTime? lastConnected;
    [ObservableProperty] private bool calendarOpen;
    [ObservableProperty] private string? collectBy;
    [ObservableProperty] private DateTime? purchaseDate;
    [ObservableProperty] private DateTime? warrantyExpiry;
    [ObservableProperty] private string? remarks;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool saving;

    public bool Saved { get; private set; }
    public AssetDetailDto? SavedAsset { get; private set; }

    public AssetEditViewModel(
        ApiClient api,
        List<CategoryDto> categories,
        List<LocationDto> locations,
        List<UserDto> users,
        List<string> assigneeNames,
        AssetListDto? existing,
        bool copy = false)
    {
        _api = api;
        _users = users;
        Categories = categories;
        Locations = [new LocationDto { Id = 0, Name = "(None)" }, .. locations];
        Title = copy ? "Copy asset" : existing is null ? "Add asset" : "Edit asset";

        AssigneeChoices =
        [
            "",
            .. assigneeNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
        ];

        CollectByChoices =
        [
            "",
            .. users.Where(u => u.IsActive && u.Role is UserRole.Engineer or UserRole.Administrator)
                .Select(u => u.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
        ];
        OfficeChoices = ["", "O365", "Office 2024", "Office 2021", "Office 2019", "Office 2016", "None"];

        if (existing is not null)
        {
            if (!copy)
            {
                _id = existing.Id;
                _version = existing.Version;
            }
            AssetTag = copy ? "" : existing.AssetTag;
            SrNoText = copy ? null : existing.SrNo?.ToString();
            CategoryId = existing.CategoryId;
            Manufacturer = existing.Manufacturer;
            Model = existing.Model;
            SerialNumber = copy ? null : existing.SerialNumber;
            Hostname = copy ? null : existing.Hostname;
            IpAddress = copy ? null : existing.IpAddress;
            LocationId = existing.LocationId ?? 0;
            Status = existing.Status;
            AssignedUserId = existing.AssignedUserId;
            AssignedUserName = existing.AssignedUser;
            Designation = existing.Designation;
            AlternateUser = existing.AlternateUser;
            Domain = existing.Domain;
            MacAddress = copy ? null : existing.MacAddress;
            Processor = existing.Processor;
            Ram = existing.Ram;
            Storage = existing.Storage;
            OperatingSystem = existing.OperatingSystem;
            DcInstalled = existing.DcInstalled ?? "";
            AvInstalled = existing.AvInstalled ?? "";
            MsOfficeVersion = string.IsNullOrWhiteSpace(existing.MsOfficeVersion) ? "O365" : existing.MsOfficeVersion;
            MfaEnabled = existing.MfaEnabled ?? "";
            IvantiInstalled = existing.IvantiInstalled ?? "";
            AdminRights = existing.AdminRights ?? "";
            UsbAccess = existing.UsbAccess ?? "";
            ChromeUpdated = existing.ChromeUpdated ?? "";
            PmCompleted = existing.PmCompleted ?? "";
            LastConnected = copy ? null : existing.LastConnected;
            CollectBy = existing.CollectBy ?? "";
            PurchaseDate = existing.PurchaseDate;
            WarrantyExpiry = existing.WarrantyExpiry;
            Remarks = existing.Remarks;
            if (!string.IsNullOrWhiteSpace(AssignedUserName) &&
                !AssigneeChoices.Contains(AssignedUserName, StringComparer.OrdinalIgnoreCase))
                AssigneeChoices.Insert(1, AssignedUserName);
            if (!string.IsNullOrWhiteSpace(CollectBy) &&
                !CollectByChoices.Contains(CollectBy, StringComparer.OrdinalIgnoreCase))
                CollectByChoices.Insert(1, CollectBy);
            if (!string.IsNullOrWhiteSpace(MsOfficeVersion) &&
                !OfficeChoices.Contains(MsOfficeVersion, StringComparer.OrdinalIgnoreCase))
                OfficeChoices.Insert(1, MsOfficeVersion);
            if (copy)
                Error = "Copied. Enter a new Asset ID / serial / hostname, then save.";
        }
        else
        {
            CategoryId = categories.FirstOrDefault()?.Id ?? 0;
            MsOfficeVersion = "O365";
            DcInstalled = "";
            AvInstalled = "";
            MfaEnabled = "";
            IvantiInstalled = "";
            AdminRights = "";
            UsbAccess = "";
            ChromeUpdated = "";
            PmCompleted = "";
            CollectBy = "";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(AssetTag) && string.IsNullOrWhiteSpace(SerialNumber) && string.IsNullOrWhiteSpace(Hostname))
        {
            Error = "Enter an Asset ID, serial number, or hostname.";
            return;
        }
        if (CategoryId == 0)
        {
            Error = "Category is required.";
            return;
        }
        if (!AssetStatusNames.TryParse(Status, out var status))
        {
            Error = "Status is required.";
            return;
        }

        int? srNo = null;
        if (!string.IsNullOrWhiteSpace(SrNoText))
        {
            if (!int.TryParse(SrNoText, out var n))
            {
                Error = "Sr No must be a number.";
                return;
            }
            srNo = n;
        }

        var tag = string.IsNullOrWhiteSpace(AssetTag)
            ? SerialNumber?.Trim() ?? Hostname!.Trim()
            : AssetTag.Trim();

        var req = new SaveAssetRequest
        {
            AssetTag = tag,
            SrNo = srNo,
            CategoryId = CategoryId,
            Manufacturer = Manufacturer,
            Model = Model,
            SerialNumber = SerialNumber,
            Hostname = Hostname,
            IpAddress = IpAddress,
            LocationId = LocationId == 0 ? null : LocationId,
            Status = status,
            AssignedUserId = ResolveAssignedUserId(),
            AssignedUserName = AssignedUserName,
            Designation = Designation,
            AlternateUser = AlternateUser,
            Domain = Domain,
            MacAddress = MacAddress,
            Processor = Processor,
            Ram = Ram,
            Storage = Storage,
            OperatingSystem = OperatingSystem,
            DcInstalled = DcInstalled,
            AvInstalled = AvInstalled,
            MsOfficeVersion = MsOfficeVersion,
            MfaEnabled = MfaEnabled,
            IvantiInstalled = IvantiInstalled,
            AdminRights = AdminRights,
            UsbAccess = UsbAccess,
            ChromeUpdated = ChromeUpdated,
            PmCompleted = PmCompleted,
            LastConnected = LastConnected,
            CollectBy = CollectBy,
            PurchaseDate = PurchaseDate,
            WarrantyExpiry = WarrantyExpiry,
            Remarks = Remarks,
            Version = _version
        };

        Saving = true;
        try
        {
            SavedAsset = _id is null
                ? await _api.CreateAssetAsync(req)
                : await _api.UpdateAssetAsync(_id.Value, req);
            Saved = true;
            CloseRequested?.Invoke(true);
        }
        catch (ApiException ex) when (ex.IsConflict)
        {
            Error = "This asset was modified by another user." + Environment.NewLine +
                    "Please refresh the asset before saving.";
        }
        catch (Exception ex)
        {
            Error = ex is ApiException api && api.Errors is { Count: > 0 }
                ? string.Join(Environment.NewLine, api.Errors)
                : ex.Message;
        }
        finally
        {
            Saving = false;
        }
    }

    private int? ResolveAssignedUserId()
    {
        if (string.IsNullOrWhiteSpace(AssignedUserName))
            return null;
        var key = AssignedUserName.Trim();
        var matches = _users
            .Where(u =>
                string.Equals(u.Name, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Username, key, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Id)
            .Distinct()
            .ToList();
        if (matches.Count == 1)
            return matches[0];
        if (AssignedUserId is { } id && _users.Any(u => u.Id == id))
            return id;
        return null;
    }

    partial void OnAssignedUserNameChanged(string? value)
    {
        if (_lockName || string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2) return;
        var key = value.Trim();
        var hits = AssigneeChoices
            .Where(n => n.Contains(key, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (hits.Count == 1 && !hits[0].Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            _lockName = true;
            AssignedUserName = hits[0];
            _lockName = false;
        }
    }

    public string LastConnectedText
    {
        get => LastConnected?.ToString("dd-MMM-yyyy") ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                LastConnected = null;
                return;
            }
            if (DateTime.TryParse(value, out var d))
                LastConnected = d.Date;
        }
    }

    partial void OnLastConnectedChanged(DateTime? value)
    {
        CalendarOpen = false;
        OnPropertyChanged(nameof(LastConnectedText));
    }

    [RelayCommand]
    private void LastConnectedToday() => LastConnected = DateTime.Today;

    [RelayCommand]
    private void ClearLastConnected() => LastConnected = null;

    [RelayCommand]
    private void ToggleCalendar() => CalendarOpen = !CalendarOpen;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    public event Action<bool>? CloseRequested;
}
