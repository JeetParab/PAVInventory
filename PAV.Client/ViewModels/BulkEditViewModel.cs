using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Core.Services;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class BulkEditViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly List<int> _ids;
    private readonly List<UserDto> _users;

    public const string Unchanged = "(unchanged)";
    public const string Unassigned = "(Unassigned)";

    public string Title => $"Bulk edit {_ids.Count} assets";
    public List<string> Statuses { get; }
    public List<LocationDto> Locations { get; }
    public List<string> AssigneeChoices { get; }
    public List<string> YesNo { get; } = [Unchanged, "Yes", "No"];

    [ObservableProperty] private string status = Unchanged;
    [ObservableProperty] private int locationId = -1;
    [ObservableProperty] private string assignedUserName = Unchanged;
    [ObservableProperty] private string? designation;
    [ObservableProperty] private string? hostname;
    [ObservableProperty] private string? ipAddress;
    [ObservableProperty] private string? domain;
    [ObservableProperty] private string mfaEnabled = Unchanged;
    [ObservableProperty] private string adminRights = Unchanged;
    [ObservableProperty] private string usbAccess = Unchanged;
    [ObservableProperty] private DateTime? lastConnected;
    [ObservableProperty] private string? collectBy;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool saving;

    public event Action<bool>? CloseRequested;

    public BulkEditViewModel(ApiClient api, List<int> ids, List<LocationDto> locations, List<UserDto> users, List<string> assigneeNames)
    {
        _api = api;
        _ids = ids;
        _users = users;
        Locations = [new LocationDto { Id = -1, Name = Unchanged }, new LocationDto { Id = 0, Name = "(none)" }, .. locations];
        Statuses = [Unchanged, .. AssetStatusNames.All.Select(s => s.Display())];
        AssigneeChoices =
        [
            Unchanged,
            Unassigned,
            .. users
                .Where(u => !string.IsNullOrWhiteSpace(u.Name))
                .Select(u => u.AssignLabel)
                .Concat(assigneeNames)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
        ];
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        Error = null;
        var req = new BulkEditRequest { Ids = _ids };
        if (Status != Unchanged && AssetStatusNames.TryParse(Status, out var st))
            req.Status = st;
        if (LocationId != -1)
        {
            req.SetLocation = true;
            req.LocationId = LocationId == 0 ? null : LocationId;
        }

        var assign = AssignedUserName?.Trim();
        if (!string.IsNullOrWhiteSpace(assign) && !assign.Equals(Unchanged, StringComparison.OrdinalIgnoreCase))
        {
            req.SetAssignedUser = true;
            if (assign.Equals(Unassigned, StringComparison.OrdinalIgnoreCase))
            {
                req.AssignedUserId = null;
                req.AssignedUserName = null;
            }
            else
            {
                req.AssignedUserName = assign;
                var tuples = _users.Select(u => (u.Id, u.Name, u.Username, u.SamAccount)).ToList();
                req.AssignedUserId = UserNameResolver.ResolveUniqueId(tuples, assign);
                if (req.AssignedUserId is { } uid)
                    req.AssignedUserName = _users.First(u => u.Id == uid).Name;
            }
        }

        if (!string.IsNullOrWhiteSpace(Designation)) req.Designation = Designation;
        if (!string.IsNullOrWhiteSpace(Hostname)) req.Hostname = Hostname;
        if (!string.IsNullOrWhiteSpace(IpAddress)) req.IpAddress = IpAddress;
        if (!string.IsNullOrWhiteSpace(Domain)) req.Domain = Domain;
        if (MfaEnabled != Unchanged) req.MfaEnabled = MfaEnabled;
        if (AdminRights != Unchanged) req.AdminRights = AdminRights;
        if (UsbAccess != Unchanged) req.UsbAccess = UsbAccess;
        if (LastConnected is not null)
        {
            req.SetLastConnected = true;
            req.LastConnected = LastConnected;
        }
        if (!string.IsNullOrWhiteSpace(CollectBy)) req.CollectBy = CollectBy;

        Saving = true;
        try
        {
            var n = await _api.BulkPatchAsync(req);
            Ui.Info($"Updated {n} assets.");
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Saving = false;
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

    partial void OnLastConnectedChanged(DateTime? value) => OnPropertyChanged(nameof(LastConnectedText));

    [RelayCommand]
    private void LastConnectedToday() => LastConnected = DateTime.Today;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
