using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class IpInventoryView : UserControl
{
    public IpInventoryView() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is IpInventoryViewModel vm && vm.OpenRowCommand.CanExecute(null))
            vm.OpenRowCommand.Execute(null);
    }
}
