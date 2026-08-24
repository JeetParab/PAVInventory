using System.Windows.Controls;
using System.Windows.Input;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class StockView : UserControl
{
    public StockView() => InitializeComponent();

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is StockViewModel vm && vm.EditItemCommand.CanExecute(null))
            vm.EditItemCommand.Execute(null);
    }
}
