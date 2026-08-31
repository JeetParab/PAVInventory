using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void OnGridPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject d) return;
        var sv = FindScrollViewer(d);
        if (sv is null) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        if (e.Delta < 0 && sv.VerticalOffset >= sv.ScrollableHeight && sv.ScrollableWidth > 0)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset + 48);
            e.Handled = sv.HorizontalOffset > 0 || sv.ScrollableWidth > 0;
        }
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
