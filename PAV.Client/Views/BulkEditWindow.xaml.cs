using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class BulkEditWindow : Window
{
    public BulkEditWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BulkEditViewModel vm)
                vm.CloseRequested += r => { try { DialogResult = r; } catch { Close(); } };
        };
    }
}
