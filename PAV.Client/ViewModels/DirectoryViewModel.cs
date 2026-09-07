using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class DirectoryViewModel(ApiClient api, ShellViewModel shell) : ObservableObject
{
    private readonly List<AdUserDto> _all = [];

    public ObservableCollection<AdUserDto> Users { get; } = [];

    [ObservableProperty] private AdUserDto? selected;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string summary = "AD snapshot used to match ManageEngine last logon. Edit or remove rows here — this is not the PAV users list.";

    public bool CanImportAd => shell.CanImport;
    public bool CanAdd => shell.CanAdd;
    public bool CanEditAd => shell.CanEdit && Selected is not null;
    public bool CanDeleteAd => shell.CanDelete && Selected is not null;
    public bool CanAddToPav => CanAdd && Selected is not null && !Selected.InPav;

    public async Task LoadAsync()
    {
        Loading = true;
        var keep = Selected?.Sam;
        try
        {
            var rows = await api.AdDirectoryAsync();
            _all.Clear();
            _all.AddRange(rows);
            RaiseCan();
            ApplyFilter();
            if (keep is { } sam)
                Selected = Users.FirstOrDefault(u => string.Equals(u.Sam, sam, StringComparison.OrdinalIgnoreCase))
                           ?? Users.FirstOrDefault();
            else
                Selected = Users.FirstOrDefault();
            Summary = _all.Count == 0
                ? "No AD user ids stored yet. Import AD users, or add one."
                : $"{_all.Count:N0} AD user ids. Search, edit or remove. PAV users is the other tab — people you assign kit to.";
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
    partial void OnSelectedChanged(AdUserDto? value) => RaiseCan();

    private void RaiseCan()
    {
        OnPropertyChanged(nameof(CanImportAd));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanEditAd));
        OnPropertyChanged(nameof(CanDeleteAd));
        OnPropertyChanged(nameof(CanAddToPav));
    }

    private void ApplyFilter()
    {
        var s = Search?.Trim() ?? "";
        IEnumerable<AdUserDto> q = _all;
        if (s.Length > 0)
        {
            q = _all.Where(u =>
                u.Sam.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (u.Name?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Email?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Department?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.EmployeeId?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var keep = Selected?.Sam;
        Users.Clear();
        foreach (var u in q) Users.Add(u);
        if (s.Length > 0)
            Selected = Users.FirstOrDefault();
        else if (keep is { } sam)
            Selected = Users.FirstOrDefault(u => string.Equals(u.Sam, sam, StringComparison.OrdinalIgnoreCase))
                       ?? Users.FirstOrDefault();
        else
            Selected = Users.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task ImportAdAsync()
    {
        if (!CanImportAd) return;
        var path = Ui.OpenExcel();
        if (path is null) return;
        try
        {
            var preview = await api.PreviewAdImportAsync(path);
            var vm = new AdImportViewModel(api, path, preview);
            var win = new AdImportWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            if (win.ShowDialog() == true)
            {
                if (!string.IsNullOrWhiteSpace(vm.ResultSummary))
                    Ui.Info(vm.ResultSummary);
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanAdd) return;
        await EditRow(null);
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (!CanEditAd || Selected is null) return;
        await EditRow(Selected);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanDeleteAd || Selected is null) return;
        var label = string.IsNullOrWhiteSpace(Selected.Name) ? Selected.Sam : $"{Selected.Name} ({Selected.Sam})";
        if (!Ui.Confirm($"Remove {label} from AD users?\n\nThis only deletes the AD snapshot. A PAV user with the same id is left as-is."))
            return;
        try
        {
            await api.DeleteAdUserAsync(Selected.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task AddToPavAsync()
    {
        if (!CanAddToPav || Selected is null) return;
        try
        {
            var person = await api.EnsurePersonFromAdAsync(Selected.Sam);
            if (person is null)
            {
                Ui.Info("Could not add that user id to PAV users.");
                return;
            }
            Ui.Info($"{person.Name} is now on the PAV users tab.");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    private async Task EditRow(AdUserDto? existing)
    {
        var vm = new AdUserEditViewModel(api, existing);
        var win = new AdUserEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
            await LoadAsync();
    }
}

public partial class AdUserEditViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly int? _id;

    public string Title => _id is null ? "Add AD user" : "Edit AD user";
    [ObservableProperty] private string sam = "";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string? employeeId;
    [ObservableProperty] private string? email;
    [ObservableProperty] private string? department;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool saving;
    public event Action<bool>? CloseRequested;

    public AdUserEditViewModel(ApiClient api, AdUserDto? existing)
    {
        _api = api;
        if (existing is not null)
        {
            _id = existing.Id;
            Sam = existing.Sam;
            Name = existing.Name ?? "";
            EmployeeId = existing.EmployeeId;
            Email = existing.Email;
            Department = existing.Department;
            IsActive = existing.IsActive;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Sam))
        {
            Error = "User ID is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Name is required.";
            return;
        }
        Saving = true;
        try
        {
            await _api.SaveAdUserAsync(_id, new SaveAdUserRequest
            {
                Sam = Sam.Trim(),
                Name = Name.Trim(),
                EmployeeId = EmployeeId,
                Email = Email,
                Department = Department,
                IsActive = IsActive
            });
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
