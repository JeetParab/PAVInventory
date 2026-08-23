using System.Windows;
using System.Windows.Input;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class LoginWindow : Window
{
    private bool _closing;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
            await vm.PrepareAsync();
        if (string.IsNullOrWhiteSpace(UsernameBox.Text))
            UsernameBox.Focus();
        else
            PasswordBox.Focus();
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        if (DataContext is not LoginViewModel vm) return;
        if (vm.Busy) return;

        var ok = await vm.SignInAsync(PasswordBox.Password, ConfirmBox.Password);
        if (!ok) return;

        _closing = true;
        DialogResult = true;
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnSignIn(sender, e);
        }
    }
}
