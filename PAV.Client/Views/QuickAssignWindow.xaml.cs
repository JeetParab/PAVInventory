using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class QuickAssignWindow : Window
{
    public QuickAssignWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UserBox.Focus();
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is QuickAssignViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
