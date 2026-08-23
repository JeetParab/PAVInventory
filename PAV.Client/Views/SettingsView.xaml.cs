using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PAV.Client.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    // DataGrid swallows the mouse wheel so the page feels stuck / janky.
    // Move the outer ScrollViewer by offset instead of re-raising routed events.
    private void OnNestedPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || RootScroll is null) return;

        e.Handled = true;

        var offset = RootScroll.VerticalOffset - e.Delta;
        if (offset < 0) offset = 0;
        var max = RootScroll.ScrollableHeight;
        if (offset > max) offset = max;

        RootScroll.ScrollToVerticalOffset(offset);
    }
}
