using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class StockItemEditWindow : Window
{
    public StockItemEditWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is StockItemEditViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
