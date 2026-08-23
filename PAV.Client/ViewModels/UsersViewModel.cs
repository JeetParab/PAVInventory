using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class UsersViewModel(ApiClient api, ShellViewModel shell) : ObservableObject
{
    public ObservableCollection<UserDto> Users { get; } = [];
    public ObservableCollection<LocationDto> Locations { get; } = [];
    [ObservableProperty] private UserDto? selected;
    [ObservableProperty] private bool loading;

    public bool CanManage => shell.CanManageUsers;

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            var users = await api.UsersAsync();
            var locs = await api.LocationsAsync();
            Users.Clear();
            foreach (var u in users) Users.Add(u);
            Locations.Clear();
            foreach (var l in locs) Locations.Add(l);
            OnPropertyChanged(nameof(CanManage));
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            Loading = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanManage) return;
        await EditUser(null);
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null || !CanManage) return;
        await EditUser(Selected);
    }

    private async Task EditUser(UserDto? existing)
    {
        var vm = new UserEditViewModel(api, Locations.ToList(), existing);
        var win = new UserEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
            await LoadAsync();
    }
}

public partial class UserEditViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly int? _id;

    public string Title => _id is null ? "Add user" : "Edit user";
    public List<LocationDto> Locations { get; }
    public List<UserRole> Roles { get; } = [UserRole.Administrator, UserRole.Engineer, UserRole.Guest];

    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string? employeeId;
    [ObservableProperty] private string? email;
    [ObservableProperty] private string? department;
    [ObservableProperty] private int locationId;
    [ObservableProperty] private UserRole role = UserRole.Engineer;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool saving;
    public event Action<bool>? CloseRequested;

    public UserEditViewModel(ApiClient api, List<LocationDto> locations, UserDto? existing)
    {
        _api = api;
        Locations = [new LocationDto { Id = 0, Name = "(None)" }, .. locations];
        if (existing is not null)
        {
            _id = existing.Id;
            Username = existing.Username;
            Name = existing.Name;
            EmployeeId = existing.EmployeeId;
            Email = existing.Email;
            Department = existing.Department;
            LocationId = existing.LocationId ?? 0;
            Role = existing.Role;
            IsActive = existing.IsActive;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Name))
        {
            Error = "Username and name are required.";
            return;
        }
        if (_id is null && string.IsNullOrWhiteSpace(Password))
        {
            Error = "Password is required for a new user.";
            return;
        }
        Saving = true;
        try
        {
            var req = new SaveUserRequest
            {
                Username = Username.Trim(),
                Password = string.IsNullOrWhiteSpace(Password) ? null : Password,
                Name = Name.Trim(),
                EmployeeId = EmployeeId,
                Email = Email,
                Department = Department,
                LocationId = LocationId == 0 ? null : LocationId,
                Role = Role,
                IsActive = IsActive
            };
            if (_id is null) await _api.CreateUserAsync(req);
            else await _api.UpdateUserAsync(_id.Value, req);
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

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
