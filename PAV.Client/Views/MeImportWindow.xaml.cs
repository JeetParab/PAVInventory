using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class MeImportWindow : Window
{
    public MeImportWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MeImportViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
