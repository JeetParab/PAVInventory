using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class UserEditWindow : Window
{
    public UserEditWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is UserEditViewModel vm)
                vm.CloseRequested += r =>
                {
                    try { DialogResult = r; } catch { }
                    Close();
                };
        };
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UserEditViewModel vm && sender is PasswordBox box)
            vm.Password = box.Password;
    }
}
