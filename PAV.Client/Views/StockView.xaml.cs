using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class StockView : UserControl
{
    private static readonly HashSet<string> AlwaysHeaders = new(StringComparer.Ordinal)
    {
        "Item", "On hand", "Issued"
    };

    public StockView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Hook();
            ApplyColumns();
        };
        DataContextChanged += (_, _) =>
        {
            Hook();
            ApplyColumns();
        };
    }

    private void Hook()
    {
        if (DataContext is not StockViewModel vm) return;
        vm.PropertyChanged -= OnVmChanged;
        vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StockViewModel.ShowDetailColumns) or null)
            ApplyColumns();
    }

    private void ApplyColumns()
    {
        if (ItemGrid is null) return;
        var showAll = DataContext is StockViewModel { ShowDetailColumns: true };
        foreach (var col in ItemGrid.Columns)
        {
            var header = col.Header?.ToString() ?? "";
            if (AlwaysHeaders.Contains(header))
            {
                col.Visibility = Visibility.Visible;
                continue;
            }
            col.Visibility = showAll ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is StockViewModel vm && vm.EditItemCommand.CanExecute(null))
            vm.EditItemCommand.Execute(null);
    }
}
