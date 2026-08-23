using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class ImportWizardWindow : Window
{
    public ImportWizardWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ImportWizardViewModel vm)
                vm.CloseRequested += r =>
                {
                    try { DialogResult = r; } catch { }
                    Close();
                };
        };
    }
}
