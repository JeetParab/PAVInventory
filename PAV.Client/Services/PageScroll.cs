using System.Runtime.CompilerServices;

namespace PAV.Client.Services;

/// <summary>
/// Pages: 48px per notch (Settings/forms).
/// DataGrids: one native LineUp/LineDown per notch — slower than WPF's default 3 rows,
/// without replacing the virtualizing scroll path (that felt laggy).
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

    private static readonly ConditionalWeakTable<DataGrid, ScrollViewer> Scrollers = new();
    private static readonly ConditionalWeakTable<DataGrid, Acc> Accumulators = new();

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
        if (InComboPopup(e.OriginalSource as DependencyObject))
            return;
        if (sender is ScrollViewer sv)
            Apply(sv, e);
    }

    private static void OnGridWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DataGrid grid) return;
        if (InComboPopup(e.OriginalSource as DependencyObject))
            return;
        var sv = ScrollerFor(grid);
        if (sv is null) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ApplyHorizontal(sv, e);
            return;
        }

        if (sv.ScrollableHeight <= 0) return;

        var acc = Accumulators.GetOrCreateValue(grid);
        acc.Delta += e.Delta;
        var moved = false;
        while (acc.Delta >= 120)
        {
            if (sv.VerticalOffset <= 0) { acc.Delta = 0; break; }
            sv.LineUp();
            acc.Delta -= 120;
            moved = true;
        }
        while (acc.Delta <= -120)
        {
            if (sv.VerticalOffset >= sv.ScrollableHeight) { acc.Delta = 0; break; }
            sv.LineDown();
            acc.Delta += 120;
            moved = true;
        }
        if (moved)
            e.Handled = true;
    }

    private static ScrollViewer? ScrollerFor(DataGrid grid)
    {
        if (Scrollers.TryGetValue(grid, out var sv))
            return sv;
        sv = FindScrollViewer(grid);
        if (sv is not null)
            Scrollers.Add(grid, sv);
        return sv;
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
            return ApplyHorizontal(sv, e);

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

    private static bool InComboPopup(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is System.Windows.Controls.Primitives.Popup)
                return true;
            if (d is ComboBox cb && cb.IsDropDownOpen)
                return true;
            var name = d.GetType().Name;
            if (name is "PopupRoot" or "Popup")
                return true;
            var parent = VisualTreeHelper.GetParent(d);
            if (parent is null && d is FrameworkElement fe)
                parent = fe.Parent;
            d = parent;
        }
        return false;
    }

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : v > max ? max : v;

    private sealed class Acc
    {
        public int Delta;
    }
}
