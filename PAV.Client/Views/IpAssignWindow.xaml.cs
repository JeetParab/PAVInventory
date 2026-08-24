using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class IpAssignWindow : Window
{
    public IpAssignWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is IpAssignViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
