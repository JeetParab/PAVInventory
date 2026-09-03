using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PAV.Client.Services;

/// <summary>
/// One wheel notch = ~48px on pages, or one row on DataGrids.
/// Native WPF pages jump 120px and grids jump three rows — mixed feel.
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

    public static readonly DependencyProperty GridProperty =
        DependencyProperty.RegisterAttached(
            "Grid",
            typeof(bool),
            typeof(PageScroll),
            new PropertyMetadata(false, OnGridChanged));

    public static void SetGrid(DependencyObject d, bool value) => d.SetValue(GridProperty, value);
    public static bool GetGrid(DependencyObject d) => (bool)d.GetValue(GridProperty);

    private static void OnSmoothChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        sv.PreviewMouseWheel -= OnSmoothWheel;
        if (e.NewValue is true)
            sv.PreviewMouseWheel += OnSmoothWheel;
    }

    private static void OnGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;
        grid.PreviewMouseWheel -= OnGridWheel;
        if (e.NewValue is true)
            grid.PreviewMouseWheel += OnGridWheel;
    }

    private static void OnSmoothWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
            Apply(sv, e);
    }

    private static void OnGridWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DataGrid grid) return;
        var sv = FindScrollViewer(grid);
        if (sv is null) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ApplyHorizontal(sv, e);
            return;
        }

        ApplyGrid(sv, e);
    }

    public static double Step(int delta) => PixelsPerNotch * delta / 120.0;

    /// <summary>One DataGrid row (or a fraction on a precision pad) per notch.</summary>
    public static double RowStep(int delta) => delta / 120.0;

    public static bool Apply(ScrollViewer sv, MouseWheelEventArgs e)
    {
        if (e.Handled || sv is null) return false;
        var step = Step(e.Delta);
        if (step == 0) return false;

        var horizontal = Keyboard.Modifiers == ModifierKeys.Shift
                         || (sv.ScrollableHeight <= 0 && sv.ScrollableWidth > 0);
        if (horizontal)
            return ApplyHorizontal(sv, e);

        if (sv.ScrollableHeight <= 0) return false;
        var next = Clamp(sv.VerticalOffset - step, 0, sv.ScrollableHeight);
        if (Math.Abs(next - sv.VerticalOffset) < 0.01) return false;
        sv.ScrollToVerticalOffset(next);
        e.Handled = true;
        return true;
    }

    public static bool ApplyGrid(ScrollViewer sv, MouseWheelEventArgs e)
    {
        if (e.Handled || sv is null) return false;
        var step = sv.CanContentScroll ? RowStep(e.Delta) : Step(e.Delta);
        if (step == 0) return false;
        if (sv.ScrollableHeight <= 0) return false;
        var next = Clamp(sv.VerticalOffset - step, 0, sv.ScrollableHeight);
        if (Math.Abs(next - sv.VerticalOffset) < 0.01) return false;
        sv.ScrollToVerticalOffset(next);
        e.Handled = true;
        return true;
    }

    public static bool ApplyHorizontal(ScrollViewer sv, MouseWheelEventArgs e)
    {
        if (e.Handled || sv is null || sv.ScrollableWidth <= 0) return false;
        var step = Step(e.Delta);
        if (step == 0) return false;
        var next = Clamp(sv.HorizontalOffset - step, 0, sv.ScrollableWidth);
        if (Math.Abs(next - sv.HorizontalOffset) < 0.01) return false;
        sv.ScrollToHorizontalOffset(next);
        e.Handled = true;
        return true;
    }

    public static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : v > max ? max : v;
}
