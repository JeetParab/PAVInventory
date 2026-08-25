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
    public ObservableCollection<string> DeviceTypes { get; } = new(IpAddressService.DeviceTypes);

    public bool Saved { get; private set; }
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
        _ = RefreshStatusAsync();
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
            await _api.IpAssignAsync(new AssignIpRequest
            {
                Id = null,
                RangeId = null,
                Address = Address.Trim(),
                AssignedDevice = AssignedDevice,
                AssignedUser = AssignedUser,
                Department = Department,
                MacAddress = MacAddress,
                DeviceType = DeviceType,
                Notes = Notes
            });
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
