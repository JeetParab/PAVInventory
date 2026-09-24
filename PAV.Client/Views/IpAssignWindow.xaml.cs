using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class IpAssignWindow : Window
{
    public IpAssignWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AddressBox.Focus();
            AddressBox.CaretIndex = AddressBox.Text?.Length ?? 0;
            if (string.IsNullOrWhiteSpace(AddressBox.Text))
                AddressBox.SelectAll();
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is IpAssignViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
