using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CurrentBox.Focus();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ChangePasswordViewModel vm)
                vm.CloseRequested += OnCloseRequested;
        };
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChangePasswordViewModel vm) return;
        await vm.SaveAsync(CurrentBox.Password, NewBox.Password, ConfirmBox.Password);
    }

    private void OnCloseRequested(bool result)
    {
        try { DialogResult = result; }
        catch { Close(); }
    }
}
