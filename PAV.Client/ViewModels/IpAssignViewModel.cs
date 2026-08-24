using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Core.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class IpAssignViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly int? _id;
    private readonly int? _rangeId;

    [ObservableProperty] private string title = "Assign IP";
    [ObservableProperty] private string address = "";
    [ObservableProperty] private string? assignedDevice;
    [ObservableProperty] private string? assignedUser;
    [ObservableProperty] private string? department;
    [ObservableProperty] private string? macAddress;
    [ObservableProperty] private string? deviceType;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? error;
    public ObservableCollection<string> DeviceTypes { get; } = new(IpAddressService.DeviceTypes);

    public bool Saved { get; private set; }
    public event Action<bool>? CloseRequested;

    public IpAssignViewModel(ApiClient api, IpAddressDto? existing, int? rangeId, string? nextAddress)
    {
        _api = api;
        _id = existing?.Id;
        _rangeId = existing is null ? rangeId : null;
        Address = existing?.Address ?? nextAddress ?? "";
        AssignedDevice = existing?.AssignedDevice;
        AssignedUser = existing?.AssignedUser;
        Department = existing?.Department;
        MacAddress = existing?.MacAddress;
        DeviceType = existing?.DeviceType;
        Notes = existing?.Notes;
        Title = existing is null || existing.StatusValue == PAV.Shared.Enums.IpStatus.Free
            ? $"Assign {Address}"
            : $"IP {Address}";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        try
        {
            await _api.IpAssignAsync(new AssignIpRequest
            {
                Id = _id,
                RangeId = _rangeId,
                Address = Address,
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
