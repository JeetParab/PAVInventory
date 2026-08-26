using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Core.Data;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class SettingsViewModel(ApiClient api, ClientConfig config, ShellViewModel shell) : ObservableObject
{
    [ObservableProperty] private string databasePath = config.DatabasePath;
    [ObservableProperty] private string provider = string.IsNullOrWhiteSpace(config.Provider) ? "SQLite" : config.Provider;
    [ObservableProperty] private string sqlServerConnectionString = config.SqlServerConnectionString;
    [ObservableProperty] private string? serverMessage;
    public ObservableCollection<string> Providers { get; } = ["SQLite", "SqlServer"];
    public ObservableCollection<CategoryDto> Categories { get; } = [];
    public ObservableCollection<BackupInfo> Backups { get; } = [];
    [ObservableProperty] private CategoryDto? selectedCategory;
    [ObservableProperty] private string categoryName = "";
    [ObservableProperty] private string? categoryDescription;
    [ObservableProperty] private BackupInfo? selectedBackup;
    [ObservableProperty] private string? categoryError;
    [ObservableProperty] private string? backupMessage;
    [ObservableProperty] private bool darkMode = config.DarkMode;
    [ObservableProperty] private string sqliteSourcePath = "";

    public bool CanManageCategories => shell.CanManageCategories;
    public bool CanBackup => shell.CanBackup;
    public bool CanManageDatabase => shell.CanBackup;
    public bool ShowSqliteSettings => DatabaseSettings.ParseProvider(Provider) == DatabaseProvider.Sqlite;
    public bool ShowSqlSettings => DatabaseSettings.ParseProvider(Provider) == DatabaseProvider.SqlServer;
    public string SignedInName => shell.UserLine;
    public string SignedInRole => shell.RoleLine;

    public async Task LoadAsync()
    {
        DatabasePath = config.DatabasePath;
        Provider = string.IsNullOrWhiteSpace(config.Provider) ? "SQLite" : config.Provider;
        SqlServerConnectionString = config.SqlServerConnectionString;
        OnPropertyChanged(nameof(CanManageCategories));
        OnPropertyChanged(nameof(CanBackup));
        OnPropertyChanged(nameof(CanManageDatabase));
        OnPropertyChanged(nameof(ShowSqliteSettings));
        OnPropertyChanged(nameof(ShowSqlSettings));
        OnPropertyChanged(nameof(SignedInName));
        OnPropertyChanged(nameof(SignedInRole));
        try
        {
            var cats = await api.CategoriesAsync();
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);
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

    partial void OnProviderChanged(string value)
    {
        OnPropertyChanged(nameof(ShowSqliteSettings));
        OnPropertyChanged(nameof(ShowSqlSettings));
    }

    [RelayCommand]
    private Task SignOutAsync() => shell.SignOutAsync();

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
            await api.ApplyDatabaseAsync(new DatabaseSettings
            {
                Provider = Provider,
                SqlitePath = DatabasePath,
                SqlServerConnectionString = SqlServerConnectionString
            });
            await shell.RefreshConnectionAsync();
            ServerMessage = shell.IsConnected
                ? (ShowSqlSettings ? "Connected to SQL Server." : "Shared SQLite database is open.")
                : "Cannot open the database.";
            if (shell.IsConnected)
                await LoadAsync();
        }
        catch (Exception ex)
        {
            ServerMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task MigrateFromSqliteAsync()
    {
        if (!CanManageDatabase || !ShowSqlSettings) return;
        var path = SqliteSourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var folder = Ui.PickFolder();
            if (string.IsNullOrWhiteSpace(folder)) return;
            path = folder;
            SqliteSourcePath = folder;
        }
        if (!Ui.Confirm("Copy inventory.db into SQL Server?\n\nExisting SQL Server PAV data will be replaced. The SQLite file is not deleted."))
            return;
        try
        {
            var report = await api.MigrateSqliteToSqlServerAsync(path, replaceDestination: true);
            ServerMessage = report.Summary;
            if (report.Errors.Count > 0)
                ServerMessage += Environment.NewLine + string.Join(Environment.NewLine, report.Errors);
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
        CategoryError = null;
    }

    [RelayCommand]
    private void NewCategory()
    {
        SelectedCategory = null;
        CategoryName = "";
        CategoryDescription = null;
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
            var req = new SaveCategoryRequest { Name = CategoryName.Trim(), Description = CategoryDescription };
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
