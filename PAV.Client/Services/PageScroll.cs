using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PAV.Client.Services;

/// <summary>
/// Page/form ScrollViewers otherwise jump 120px per wheel notch (too fast).
/// DataGrid lists keep native row scrolling (~one to three rows).
/// </summary>
public static class PageScroll
{
    public const double PixelsPerNotch = 48;

    public static readonly DependencyProperty SmoothProperty =
        DependencyProperty.RegisterAttached(
            "Smooth",
            typeof(bool),
            typeof(PageScroll),
            new PropertyMetadata(false, OnSmoothChanged));

    public static void SetSmooth(DependencyObject d, bool value) => d.SetValue(SmoothProperty, value);
    public static bool GetSmooth(DependencyObject d) => (bool)d.GetValue(SmoothProperty);

    private static void OnSmoothChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        sv.PreviewMouseWheel -= OnWheel;
        if (e.NewValue is true)
            sv.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
            Apply(sv, e);
    }

    public static double Step(int delta) => PixelsPerNotch * delta / 120.0;

    public static bool Apply(ScrollViewer sv, MouseWheelEventArgs e)
    {
        if (e.Handled || sv is null) return false;
        var step = Step(e.Delta);
        if (step == 0) return false;

        var horizontal = Keyboard.Modifiers == ModifierKeys.Shift
                         || (sv.ScrollableHeight <= 0 && sv.ScrollableWidth > 0);
        if (horizontal)
        {
            if (sv.ScrollableWidth <= 0) return false;
            sv.ScrollToHorizontalOffset(Clamp(sv.HorizontalOffset - step, 0, sv.ScrollableWidth));
            e.Handled = true;
            return true;
        }

        if (sv.ScrollableHeight <= 0) return false;
        sv.ScrollToVerticalOffset(Clamp(sv.VerticalOffset - step, 0, sv.ScrollableHeight));
        e.Handled = true;
        return true;
    }

    public static bool ApplyHorizontal(ScrollViewer sv, MouseWheelEventArgs e)
    {
        if (e.Handled || sv is null || sv.ScrollableWidth <= 0) return false;
        var step = Step(e.Delta);
        sv.ScrollToHorizontalOffset(Clamp(sv.HorizontalOffset - step, 0, sv.ScrollableWidth));
        e.Handled = true;
        return true;
    }

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : v > max ? max : v;
}
