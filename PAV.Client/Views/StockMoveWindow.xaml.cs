using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class StockMoveWindow : Window
{
    public StockMoveWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is StockMoveViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
