using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Core.Services;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class IpAssignViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly int? _id;
    private int _checkSeq;
    private readonly List<string> _people = [];
    private bool _suppressFilter;

    [ObservableProperty] private string title = "Assign IP";
    [ObservableProperty] private string address = "";
    [ObservableProperty] private string? assignedDevice;
    [ObservableProperty] private string? assignedUser;
    [ObservableProperty] private string? department;
    [ObservableProperty] private string? macAddress;
    [ObservableProperty] private string? deviceType;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? error;
    [ObservableProperty] private string addressStatus = "Enter an IP in 172.16.101–107.";
    [ObservableProperty] private Brush addressStatusBrush = Brushes.Gray;
    [ObservableProperty] private bool canSave;
    [ObservableProperty] private bool isTemporary;
    [ObservableProperty] private string userHint = "";
    public bool IsInventoryPurpose
    {
        get => !IsTemporary;
        set { if (value) IsTemporary = false; }
    }

    partial void OnIsTemporaryChanged(bool value) => OnPropertyChanged(nameof(IsInventoryPurpose));
    public ObservableCollection<string> DeviceTypes { get; } = new(IpAddressService.DeviceTypes);
    public ObservableCollection<string> People { get; } = [];

    public bool Saved { get; private set; }
    public string? InventorySync { get; private set; }
    public event Action<bool>? CloseRequested;

    public IpAssignViewModel(ApiClient api, IpAddressDto? existing, int? rangeId, string? nextAddress)
    {
        _api = api;
        _id = existing?.Id;
        Address = existing?.Address ?? nextAddress ?? "";
        AssignedDevice = existing?.AssignedDevice;
        AssignedUser = existing?.AssignedUser;
        Department = existing?.Department;
        MacAddress = existing?.MacAddress;
        DeviceType = existing?.DeviceType;
        Notes = existing?.Notes;
        Title = string.IsNullOrWhiteSpace(Address) ? "Assign IP" : $"Assign {Address}";
        _ = LoadPeopleAsync();
        _ = RefreshStatusAsync();
    }

    private async Task LoadPeopleAsync()
    {
        try
        {
            var users = await _api.UsersAsync();
            _people.Clear();
            foreach (var n in users.Where(u => u.IsActive).Select(u => u.Name)
                         .Where(n => !string.IsNullOrWhiteSpace(n))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n))
                _people.Add(n);
            ApplyPeopleFilter();
        }
        catch
        {
            /* type freely if directory cannot load */
        }
    }

    partial void OnAssignedUserChanged(string? value)
    {
        ApplyPeopleFilter();
        UpdateUserHint();
    }

    private void ApplyPeopleFilter()
    {
        if (_suppressFilter) return;
        var typed = AssignedUser?.Trim() ?? "";
        People.Clear();
        IEnumerable<string> q = _people;
        if (typed.Length > 0)
            q = _people.Where(n => n.Contains(typed, StringComparison.OrdinalIgnoreCase));
        foreach (var n in q.Take(40))
            People.Add(n);

        if (typed.Length >= 2)
        {
            var hits = _people.Where(n => n.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 1 && !hits[0].Equals(typed, StringComparison.OrdinalIgnoreCase))
            {
                _suppressFilter = true;
                AssignedUser = hits[0];
                _suppressFilter = false;
                People.Clear();
                People.Add(hits[0]);
            }
        }
    }

    private void UpdateUserHint()
    {
        var typed = AssignedUser?.Trim() ?? "";
        if (typed.Length == 0)
        {
            UserHint = "";
            return;
        }
        var hits = _people.Where(n => n.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        UserHint = hits.Count == 1
            ? $"Will link to {hits[0]}."
            : hits.Count == 0
                ? "No directory match — stored as typed name."
                : $"{hits.Count} people match — keep typing.";
    }

    partial void OnAddressChanged(string value)
    {
        Title = string.IsNullOrWhiteSpace(value) ? "Assign IP" : $"Assign {value.Trim()}";
        _ = RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        var seq = ++_checkSeq;
        var typed = Address?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(typed))
        {
            AddressStatus = "Enter an IP in the Floor 1–7 pool (172.16.101–107).";
            AddressStatusBrush = Brushes.Gray;
            CanSave = false;
            return;
        }

        try
        {
            var result = await _api.IpCheckAsync(typed);
            if (seq != _checkSeq) return;
            if (!result.InPool || result.Record is null)
            {
                AddressStatus = result.Message;
                AddressStatusBrush = Brushes.IndianRed;
                CanSave = false;
                return;
            }

            if (result.Record.StatusValue == IpStatus.Free)
            {
                AddressStatus = $"Free on {result.Record.FloorName} ({result.Record.Subnet}). Ready to assign.";
                AddressStatusBrush = Brushes.SeaGreen;
                CanSave = true;
                return;
            }

            AddressStatus = $"{result.Record.Address} is {result.Record.Status}" +
                            (string.IsNullOrWhiteSpace(result.Record.AssignedUser)
                                ? "."
                                : $" — {result.Record.AssignedUser}.");
            AddressStatusBrush = Brushes.IndianRed;
            CanSave = false;
        }
        catch
        {
            if (seq != _checkSeq) return;
            AddressStatus = "Could not check this address.";
            AddressStatusBrush = Brushes.Gray;
            CanSave = !string.IsNullOrWhiteSpace(typed);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Address))
        {
            Error = "Enter an IP address.";
            return;
        }
        try
        {
            var dto = await _api.IpAssignAsync(new AssignIpRequest
            {
                Id = null,
                RangeId = null,
                Address = Address.Trim(),
                AssignedDevice = AssignedDevice,
                AssignedUser = AssignedUser,
                Department = Department,
                MacAddress = MacAddress,
                DeviceType = DeviceType,
                Notes = Notes,
                AddToInventory = !IsTemporary,
                IsTemporary = IsTemporary
            });
            InventorySync = dto.InventorySync;
            Saved = true;
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
