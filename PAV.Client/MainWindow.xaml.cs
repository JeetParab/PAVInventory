using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, FrameworkElement> _pages = new(StringComparer.Ordinal);
    private ShellViewModel? _shell;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_shell is not null)
                _shell.PropertyChanged -= OnShellChanged;
            _shell = DataContext as ShellViewModel;
            if (_shell is not null)
            {
                _shell.PropertyChanged += OnShellChanged;
                ShowPage(_shell.CurrentPage);
            }
        };
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.CurrentPage) or null)
            ShowPage(_shell?.CurrentPage);
    }

    private void ShowPage(string? page)
    {
        if (_shell is null || string.IsNullOrWhiteSpace(page)) return;
        try
        {
            if (!_pages.TryGetValue(page, out var view))
            {
                view = page switch
                {
                    "Dashboard" => new DashboardView { DataContext = _shell.Dashboard },
                    "Inventory" => new InventoryView { DataContext = _shell.Inventory },
                    "Peripherals" => new InventoryView { DataContext = _shell.Peripherals },
                    "IpInventory" => new IpInventoryView { DataContext = _shell.IpInventory },
                    "Pending" => new PendingView { DataContext = _shell.Pending },
                    "Stock" => new StockView { DataContext = _shell.Stock },
                    "Toner" => new StockView { DataContext = _shell.Toner },
                    "Users" => new UsersView { DataContext = _shell.Users },
                    "Locations" => new LocationsView { DataContext = _shell.Locations },
                    "Settings" => new SettingsView { DataContext = _shell.Settings },
                    _ => new InventoryView { DataContext = _shell.Inventory }
                };
                _pages[page] = view;
            }
            if (!ReferenceEquals(PageHost.Content, view))
                PageHost.Content = view;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "PAV Inventory", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
