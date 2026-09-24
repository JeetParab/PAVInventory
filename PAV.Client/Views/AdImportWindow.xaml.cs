using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class AdImportWindow : Window
{
    public AdImportWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AdImportViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
