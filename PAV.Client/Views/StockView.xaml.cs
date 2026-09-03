using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PAV.Client.Services;
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

    private void OnGridPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject d) return;
        var sv = FindScrollViewer(d);
        if (sv is null) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
            PageScroll.ApplyHorizontal(sv, e);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }
}
