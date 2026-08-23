using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class BulkAddWindow : Window
{
    public BulkAddWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BulkAddViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
