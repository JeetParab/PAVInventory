using System.Windows;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class AssetDetailWindow : Window
{
    public AssetDetailWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AssetDetailViewModel vm)
                vm.CloseRequested += () =>
                {
                    try { DialogResult = true; }
                    catch { Close(); }
                };
        };
    }
}
