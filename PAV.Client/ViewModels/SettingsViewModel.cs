using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Core.Data;
using PAV.Shared.Enums;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class SettingsViewModel(ApiClient api, ClientConfig config, ShellViewModel shell) : ObservableObject
{
    private List<LocationDto> _locations = [];

    [ObservableProperty] private string databasePath = config.DatabasePath;
    [ObservableProperty] private string? serverMessage;
    public ObservableCollection<CategoryDto> Categories { get; } = [];
    public ObservableCollection<BackupInfo> Backups { get; } = [];
    public ObservableCollection<UserDto> SignInUsers { get; } = [];
    [ObservableProperty] private CategoryDto? selectedCategory;
    [ObservableProperty] private string categoryName = "";
    [ObservableProperty] private string? categoryDescription;
    [ObservableProperty] private string categoryFamilyLabel = "Computers";
    public List<string> FamilyChoices { get; } = ["Computers", "Peripherals"];
    [ObservableProperty] private BackupInfo? selectedBackup;
    [ObservableProperty] private UserDto? selectedAccount;
    [ObservableProperty] private string? categoryError;
    [ObservableProperty] private string? backupMessage;
    [ObservableProperty] private bool darkMode = config.DarkMode;

    public bool CanManageCategories => shell.CanManageCategories;
    public bool CanBackup => shell.CanBackup;
    public bool CanManageDatabase => shell.CanBackup;
    public bool CanManageUsers => shell.CanManageUsers;
    public string SignedInName => shell.UserLine;
    public string SignedInRole => shell.RoleLine;

    public async Task LoadAsync()
    {
        DatabasePath = config.DatabasePath;
        OnPropertyChanged(nameof(CanManageCategories));
        OnPropertyChanged(nameof(CanBackup));
        OnPropertyChanged(nameof(CanManageDatabase));
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(SignedInName));
        OnPropertyChanged(nameof(SignedInRole));
        try
        {
            var cats = await api.CategoriesAsync();
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);
            if (CanManageUsers)
            {
                var accounts = await api.SignInUsersAsync();
                SignInUsers.Clear();
                foreach (var u in accounts) SignInUsers.Add(u);
                _locations = await api.LocationsAsync();
            }
            if (CanBackup)
            {
                var b = await api.BackupsAsync();
                Backups.Clear();
                foreach (var x in b) Backups.Add(x);
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private Task SignOutAsync() => shell.SignOutAsync();

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        if (!CanManageUsers) return;
        await EditAccount(null);
    }

    [RelayCommand]
    private async Task EditAccountAsync()
    {
        if (!CanManageUsers || SelectedAccount is null) return;
        await EditAccount(SelectedAccount);
    }

    private async Task EditAccount(UserDto? existing)
    {
        var vm = new UserEditViewModel(api, _locations, existing);
        var win = new UserEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
            await LoadAsync();
    }

    partial void OnDarkModeChanged(bool value)
    {
        Theme.Apply(value);
        config.DarkMode = value;
        try { config.SaveUi(); }

        catch { /* keep theme even if settings file is locked */ }
    }

    [RelayCommand]
    private void ToggleDarkMode() => DarkMode = !DarkMode;

    [RelayCommand]
    private void BrowseDatabase()
    {
        var folder = Ui.PickFolder();
        if (!string.IsNullOrWhiteSpace(folder))
            DatabasePath = folder;
    }

    [RelayCommand]
    private async Task SaveServerAsync()
    {
        try
        {
            await api.ApplyDatabaseAsync(new DatabaseSettings { SqlitePath = DatabasePath });
            await shell.RefreshConnectionAsync();
            ServerMessage = shell.IsConnected ? "Shared SQLite database is open." : "Cannot open the database.";
            if (shell.IsConnected)
                await LoadAsync();
        }
        catch (Exception ex)
        {
            ServerMessage = ex.Message;
        }
    }

    partial void OnSelectedCategoryChanged(CategoryDto? value)
    {
        CategoryName = value?.Name ?? "";
        CategoryDescription = value?.Description;
        CategoryFamilyLabel = value?.Family == CategoryFamily.Peripheral ? "Peripherals" : "Computers";
        CategoryError = null;
    }

    [RelayCommand]
    private void NewCategory()
    {
        SelectedCategory = null;
        CategoryName = "";
        CategoryDescription = null;
        CategoryFamilyLabel = "Computers";
    }

    [RelayCommand]
    private async Task SaveCategoryAsync()
    {
        if (!CanManageCategories) return;
        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            CategoryError = "Name is required.";
            return;
        }
        try
        {
            var req = new SaveCategoryRequest
            {
                Name = CategoryName.Trim(),
                Description = CategoryDescription,
                Family = CategoryFamilyLabel == "Peripherals" ? CategoryFamily.Peripheral : CategoryFamily.Computer
            };
            if (SelectedCategory is null) await api.CreateCategoryAsync(req);
            else await api.UpdateCategoryAsync(SelectedCategory.Id, req);
            await LoadAsync();
            NewCategory();
        }
        catch (Exception ex)
        {
            CategoryError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory is null || !CanManageCategories) return;
        if (!Ui.Confirm($"Delete category '{SelectedCategory.Name}'?"))
            return;
        try
        {
            await api.DeleteCategoryAsync(SelectedCategory.Id);
            await LoadAsync();
            NewCategory();
        }
        catch (Exception ex)
        {
            CategoryError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        if (!CanBackup) return;
        try
        {
            var info = await api.BackupNowAsync();
            BackupMessage = $"Backup created: {info.FileName}";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            BackupMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!CanBackup || SelectedBackup is null) return;
        if (!Ui.Confirm(
                $"Restore {SelectedBackup.FileName}?\n\nThis replaces the live database. A safety copy of the current database is kept first."))
            return;
        try
        {
            await api.RestoreAsync(SelectedBackup.FileName);
            BackupMessage = "Database restored. Reloading…";
            await shell.RetryAsync();
        }
        catch (Exception ex)
        {
            BackupMessage = ex.Message;
        }
    }
}
