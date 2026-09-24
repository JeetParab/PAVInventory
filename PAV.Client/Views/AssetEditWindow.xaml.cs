using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class AssetEditWindow : Window
{
    public AssetEditWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AssetTagBox.Focus();
            AssetTagBox.SelectAll();
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AssetEditViewModel vm)
                vm.CloseRequested += OnCloseRequested;
        };
    }

    private void OnCloseRequested(bool result)
    {
        try { DialogResult = result; }
        catch { /* already closed */ }
        Close();
    }
}
