using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly ClientConfig _config;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string currentPage = "Inventory";
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private string connectionLabel = "Connecting…";
    [ObservableProperty] private MeDto? me;
    [ObservableProperty] private string userLine = "";
    [ObservableProperty] private string roleLine = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool sidebarCollapsed;

    public DashboardViewModel Dashboard { get; }
    public InventoryViewModel Inventory { get; }
    public UsersViewModel Users { get; }
    public LocationsViewModel Locations { get; }
    public SettingsViewModel Settings { get; }

    public ShellViewModel(ApiClient api, ClientConfig config)
    {
        _api = api;
        _config = config;
        Dashboard = new DashboardViewModel(api, this);
        Inventory = new InventoryViewModel(api, this, config);
        Users = new UsersViewModel(api, this);
        Locations = new LocationsViewModel(api, this);
        Settings = new SettingsViewModel(api, config, this);

        _api.ConnectionChanged += connected =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyConnection(connected));
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += async (_, _) => await _api.PingAsync();
        ApplySignedInUser();
        SidebarCollapsed = config.SidebarCollapsed;
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        SidebarCollapsed = !SidebarCollapsed;
        _config.SidebarCollapsed = SidebarCollapsed;
        _config.Save();
    }

    public bool Can(string permission) => Me?.Permissions.Contains(permission) == true;
    public bool CanAdd => Can(Permissions.Add);
    public bool CanEdit => Can(Permissions.Edit);
    public bool CanDelete => Can(Permissions.Delete);
    public bool CanImport => Can(Permissions.Import);
    public bool CanExport => Can(Permissions.Export);
    public bool CanManageUsers => Can(Permissions.ManageUsers);
    public bool CanManageLocations => Can(Permissions.ManageLocations);
    public bool CanManageCategories => Can(Permissions.ManageCategories);
    public bool CanBackup => Can(Permissions.Backup);

    public async Task InitializeAsync()
    {
        using var _ = PAV.Core.Services.Perf.Measure("Shell.Initialize");
        Inventory.Loading = true;
        if (!_api.IsConnected)
            await RefreshConnectionAsync();
        else
            ApplyConnection(true);
        _timer.Start();
        if (IsConnected)
            await LoadCurrentPageAsync();
    }

    [RelayCommand]
    public async Task RetryAsync()
    {
        await RefreshConnectionAsync();
        if (IsConnected)
            await LoadCurrentPageAsync();
    }

    [RelayCommand]
    public async Task SignOutAsync()
    {
        _timer.Stop();
        await _api.LogoutAsync();
        var app = System.Windows.Application.Current;
        app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        if (PAV.Client.App.ShowLogin(_api, _config, app.MainWindow))
        {
            app.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            ApplySignedInUser();
            await InitializeAsync();
        }
        else
        {
            app.Shutdown();
        }
    }

    public async Task RefreshConnectionAsync()
    {
        var ok = await _api.PingAsync();
        ApplyConnection(ok);
        if (!ok) return;
        try
        {
            Me = await _api.MeAsync();
            ApplySignedInUser();
        }
        catch (Exception ex)
        {
            ApplyConnection(false);
            ConnectionLabel = ex.Message;
        }
    }

    private void ApplySignedInUser()
    {
        Me = _api.SignedInUser ?? Me;
        if (Me is null) return;
        UserLine = string.IsNullOrWhiteSpace(Me.Name) ? Me.Username : Me.Name;
        RoleLine = string.IsNullOrWhiteSpace(Me.RoleName) ? Me.Role.Display() : Me.RoleName;
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(CanManageLocations));
        OnPropertyChanged(nameof(CanManageCategories));
        OnPropertyChanged(nameof(CanBackup));
    }

    private void ApplyConnection(bool ok)
    {
        IsConnected = ok;
        ConnectionLabel = ok ? "Shared database" : "Cannot open the shared database";
    }

    [RelayCommand]
    public async Task FocusInventorySearchAsync()
    {
        if (CurrentPage != "Inventory")
            CurrentPage = "Inventory";
        Inventory.RequestFocusSearch();
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task GoAsync(string page)
    {
        CurrentPage = page;
        await LoadCurrentPageAsync();
    }

    public async Task ShowInventoryAsync(string? status = null, int? categoryId = null)
    {
        CurrentPage = "Inventory";
        await Inventory.ApplyDashboardFilterAsync(status, categoryId);
    }

    private async Task LoadCurrentPageAsync()
    {
        if (!IsConnected) return;
        try
        {
            Busy = true;
            switch (CurrentPage)
            {
                case "Dashboard":
                    await Dashboard.LoadAsync();
                    break;
                case "Inventory":
                    await Inventory.LoadAsync();
                    break;
                case "Users":
                    await Users.LoadAsync();
                    break;
                case "Locations":
                    await Locations.LoadAsync();
                    break;
                case "Settings":
                    await Settings.LoadAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            Busy = false;
        }
    }
}
