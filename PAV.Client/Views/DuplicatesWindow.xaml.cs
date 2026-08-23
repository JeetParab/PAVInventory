using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class DuplicatesWindow : Window
{
    public DuplicatesWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DuplicatesViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
