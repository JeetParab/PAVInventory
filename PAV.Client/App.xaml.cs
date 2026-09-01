using System.Windows;
using System.Windows.Threading;
using PAV.Client.Services;
using PAV.Client.ViewModels;
using PAV.Client.Views;

namespace PAV.Client;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ShowError(ex);
        };

        var config = ClientConfig.Load();
        Theme.Apply(config.DarkMode);
        var api = new ApiClient(config);
        PAV.Core.Services.Perf.Mark("App.OnStartup");

        if (!ShowLogin(api, config, owner: null))
        {
            Shutdown();
            return;
        }

        var shell = new ShellViewModel(api, config);
        var window = new MainWindow { DataContext = shell, Opacity = 0 };
        MainWindow = window;
        window.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await shell.InitializeAsync();
            }
            finally
            {
                window.Opacity = 1;
            }
        });
    }

    internal static bool ShowLogin(ApiClient api, ClientConfig config, Window? owner)
    {
        var loginVm = new LoginViewModel(api, config);
        var login = new LoginWindow { DataContext = loginVm };
        if (owner is not null)
            login.Owner = owner;
        if (login.ShowDialog() != true)
            return false;
        return EnsurePasswordChanged(api, owner);
    }

    internal static bool EnsurePasswordChanged(ApiClient api, Window? owner)
    {
        if (api.SignedInUser?.MustChangePassword != true)
            return true;

        var vm = new ChangePasswordViewModel(api) { Forced = true };
        var win = new ChangePasswordWindow { DataContext = vm };
        if (owner is not null)
            win.Owner = owner;
        else
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return win.ShowDialog() == true && vm.Saved;
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(
            ex.GetType().Name + ": " + (string.IsNullOrWhiteSpace(ex.Message) ? "Unexpected error." : ex.Message),
            "PAV Inventory",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
