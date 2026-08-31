using System.Windows.Controls;
using System.Windows.Input;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class PendingView : UserControl
{
    public PendingView() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PendingViewModel vm)
            vm.OpenRowCommand.Execute(null);
    }
}
