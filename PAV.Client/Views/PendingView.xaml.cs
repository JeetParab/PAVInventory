using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class PendingView : UserControl
{
    public PendingView() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (e.OriginalSource is not DependencyObject src) return;
        if (ItemsControl.ContainerFromElement(grid, src) is not DataGridRow) return;
        e.Handled = true;
        if (DataContext is PendingViewModel vm && vm.OpenRowCommand.CanExecute(null))
            vm.OpenRowCommand.Execute(null);
    }
}
