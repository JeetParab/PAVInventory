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
    private readonly List<UserDto> _all = [];
    private int _holdingsSeq;

    public ObservableCollection<UserDto> Users { get; } = [];
    public ObservableCollection<AssetListDto> Assets { get; } = [];
    public ObservableCollection<StockMovementDto> Stock { get; } = [];
    public ObservableCollection<UnlinkedAssignmentDto> Unlinked { get; } = [];
    public DirectoryViewModel Directory { get; } = new(api, shell);

    [ObservableProperty] private UserDto? selected;
    [ObservableProperty] private AssetListDto? selectedAsset;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string holdingsTitle = "Select a person to see assigned assets.";
    [ObservableProperty] private string? unlinkedSummary;
    [ObservableProperty] private bool showAdUsers;

    public bool ShowPavUsers => !ShowAdUsers;

    public bool CanAdd => shell.CanAdd;
    public bool CanEditPeople => shell.CanEdit;
    public bool CanDeletePeople => shell.CanDelete;
    public bool HasUnlinked => Unlinked.Count > 0;

    public async Task LoadAsync()
    {
        Loading = true;
        var keepId = Selected?.Id;
        try
        {
            var users = await api.PeopleAsync();
            var unlinked = await api.UnlinkedAssignmentsAsync();
            _all.Clear();
            _all.AddRange(users);
            Unlinked.Clear();
            foreach (var u in unlinked) Unlinked.Add(u);
            UnlinkedSummary = unlinked.Count == 0
                ? null
                : $"{unlinked.Sum(x => x.AssetCount)} assets use a name that is not in this list. Click Add from inventory to create them and link the assets.";
            OnPropertyChanged(nameof(CanAdd));
            OnPropertyChanged(nameof(CanEditPeople));
            OnPropertyChanged(nameof(CanDeletePeople));
            OnPropertyChanged(nameof(HasUnlinked));
            ApplyFilter();
            if (keepId is { } id)
                Selected = Users.FirstOrDefault(u => u.Id == id) ?? Users.FirstOrDefault();
            else if (Selected is null)
                Selected = Users.FirstOrDefault();
            await Directory.LoadAsync();
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

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnShowAdUsersChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPavUsers));
        if (value && Directory.Users.Count == 0)
            _ = Directory.LoadAsync();
    }

    partial void OnSelectedChanged(UserDto? value) => _ = LoadHoldingsAsync();

    private void ApplyFilter()
    {
        var s = Search?.Trim() ?? "";
        IEnumerable<UserDto> q = _all;
        if (!string.IsNullOrWhiteSpace(s))
        {
            q = _all.Where(u =>
                u.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (u.EmployeeId?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Department?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Email?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Username?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.SamAccount?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var keep = Selected?.Id;
        Users.Clear();
        foreach (var u in q) Users.Add(u);
        if (!string.IsNullOrWhiteSpace(s))
            Selected = Users.FirstOrDefault();
        else if (keep is { } id)
            Selected = Users.FirstOrDefault(u => u.Id == id) ?? Users.FirstOrDefault();
        else
            Selected = Users.FirstOrDefault();
    }

    private async Task LoadHoldingsAsync()
    {
        var seq = ++_holdingsSeq;
        var user = Selected;
        Assets.Clear();
        Stock.Clear();
        if (user is null)
        {
            HoldingsTitle = "Select a person to see assigned assets.";
            return;
        }
        HoldingsTitle = $"{user.Name}  ·  {user.AssetCount} assets  ·  {user.StockWithUser} stock with them";
        try
        {
            var query = ApiClient.Query(assignedUserId: user.Id);
            var assets = await api.AssetsAsync(query);
            var moves = await api.StockMovementsAsync(userId: user.Id);
            if (seq != _holdingsSeq) return;
            foreach (var a in assets) Assets.Add(a);
            foreach (var m in moves) Stock.Add(m);
        }
        catch (Exception ex)
        {
            if (seq == _holdingsSeq)
                Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanAdd) return;
        await EditPerson(null);
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null || !CanEditPeople) return;
        await EditPerson(Selected);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null || !CanDeletePeople) return;
        if (!Ui.Confirm($"Remove {Selected.Name} from the directory?\n\nUnassign their assets first if they still have any."))
            return;
        try
        {
            await api.DeletePersonAsync(Selected.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task ImportFromInventoryAsync()
    {
        if (!CanAdd) return;
        var pending = Unlinked.Count;
        var msg = pending > 0
            ? $"Add people from inventory?\n\n{pending} names on assets are not in this list. They will be added (name only) and their assets will be linked. Existing sign-in accounts are not duplicated."
            : "Add people from inventory?\n\nPAV will take unique names from Inventory and Stock, skip names that already exist, and link matching assets.";
        if (!Ui.Confirm(msg)) return;
        try
        {
            var result = await api.ImportPeopleFromInventoryAsync();
            Ui.Info(result.Summary);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private async Task EditPerson(UserDto? existing)
    {
        var vm = new PersonEditViewModel(api, existing);
        var win = new PersonEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
            await LoadAsync();
    }
}

public partial class PersonEditViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly int? _id;

    public string Title => _id is null ? "Add person" : "Edit person";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string? employeeId;
    [ObservableProperty] private string? email;
    [ObservableProperty] private string? department;
    [ObservableProperty] private string? samAccount;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool saving;
    public event Action<bool>? CloseRequested;

    public PersonEditViewModel(ApiClient api, UserDto? existing)
    {
        _api = api;
        if (existing is not null)
        {
            _id = existing.Id;
            Name = existing.Name;
            EmployeeId = existing.EmployeeId;
            Email = existing.Email;
            Department = existing.Department;
            SamAccount = existing.SamAccount;
            IsActive = existing.IsActive;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Name is required.";
            return;
        }
        Saving = true;
        try
        {
            var req = new SavePersonRequest
            {
                Name = Name.Trim(),
                EmployeeId = EmployeeId,
                Email = Email,
                Department = Department,
                SamAccount = SamAccount,
                IsActive = IsActive
            };
            if (_id is null) await _api.CreatePersonAsync(req);
            else await _api.UpdatePersonAsync(_id.Value, req);
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

public partial class UserEditViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly int? _id;

    public string Title => _id is null ? "Add sign-in account" : "Edit sign-in account";
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
            Error = "Password is required for a new account.";
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
