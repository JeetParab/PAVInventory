using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PAV.Client.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    // DataGrid captures the mouse wheel and makes the page feel stuck.
    // Forward the wheel to the outer page ScrollViewer so Settings scrolls smoothly.
    private void OnNestedPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        e.Handled = true;
        var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        RootScroll.RaiseEvent(args);
    }
}
