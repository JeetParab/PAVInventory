using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class StockImportWindow : Window
{
    public StockImportWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is StockImportViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
