using PAV.Client.ViewModels;
using PAV.Shared.Dtos;

namespace PAV.Client.Views;

public partial class InventoryView : UserControl
{
    private static readonly HashSet<string> ComputerOnlyHeaders = new(StringComparer.Ordinal)
    {
        "Hostname", "Purpose", "Domain", "Processor", "RAM",
        "Storage", "OS", "DC", "AV", "Office", "MFA", "Ivanti", "Admin", "USB", "Chrome",
        "PM", "Last Connected", "Collect By"
    };

    public InventoryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += (_, _) => HookVm();
        AssetGrid.ColumnReordered += OnColumnReordered;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AssetGrid.FrozenColumnCount = 0;
            ApplyColumnVisibility();
            ApplySavedOrder();
            ApplyFreeze();
            HookVm();
            DetailsToggle.IsChecked = ActualWidth >= 1400;
        }
        catch (Exception ex)
        {
            AssetGrid.FrozenColumnCount = 0;
            App.LogUnhandled(ex);
        }
    }

    private void HookVm()
    {
        if (DataContext is not InventoryViewModel vm) return;
        vm.ColumnOrderReset -= ApplyDefaultOrder;
        vm.ColumnOrderReset += ApplyDefaultOrder;
        vm.FocusSearchRequested -= OnFocusSearch;
        vm.FocusSearchRequested += OnFocusSearch;
        vm.RestoreSelectionRequested -= OnRestoreSelection;
        vm.RestoreSelectionRequested += OnRestoreSelection;
        vm.GridLayoutChanged -= ApplyFreeze;
        vm.GridLayoutChanged += ApplyFreeze;
    }

    private void OnFocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnRestoreSelection(IReadOnlyList<int> ids, int? last)
    {
        try
        {
            AssetGrid.SelectedItems.Clear();
            foreach (var item in AssetGrid.Items.OfType<AssetListDto>().Where(a => ids.Contains(a.Id)))
                AssetGrid.SelectedItems.Add(item);

            if (last is { } id)
            {
                var row = AssetGrid.Items.OfType<AssetListDto>().FirstOrDefault(a => a.Id == id);
                if (row is not null)
                    AssetGrid.ScrollIntoView(row);
            }
        }
        catch
        {
            // Grid may not be ready yet.
        }
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindRow(e.OriginalSource as DependencyObject);
        if (row is null) return;
        if (!row.IsSelected)
        {
            AssetGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private static DataGridRow? FindRow(DependencyObject? src)
    {
        while (src is not null)
        {
            if (src is DataGridRow row) return row;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is InventoryViewModel vm && sender is DataGrid grid)
            vm.SetSelected(grid.SelectedItems.OfType<AssetListDto>().ToList());
    }

    private void OnColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        if (DataContext is not InventoryViewModel vm || !vm.IsComputers) return;
        var headers = AssetGrid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Select(c => c.Header?.ToString() ?? "")
            .ToList();
        vm.SaveColumnOrder(headers);
    }

    private void ApplyColumnVisibility()
    {
        if (DataContext is not InventoryViewModel vm || vm.IsComputers) return;
        AssetGrid.FrozenColumnCount = 0;
        foreach (var col in AssetGrid.Columns)
        {
            var header = col.Header?.ToString() ?? "";
            if (ComputerOnlyHeaders.Contains(header) && col.Visibility != Visibility.Collapsed)
                col.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFreeze()
    {
        if (DataContext is not InventoryViewModel vm)
        {
            AssetGrid.FrozenColumnCount = 0;
            return;
        }
        try
        {
            AssetGrid.FrozenColumnCount = 0;
            if (!vm.IsComputers || !vm.FreezeIdentityColumns) return;
            var visible = 0;
            foreach (var col in AssetGrid.Columns.OrderBy(c => c.DisplayIndex))
            {
                if (col.Visibility != Visibility.Visible) break;
                visible++;
                if (visible >= 6) break;
            }
            if (visible > 0)
                AssetGrid.FrozenColumnCount = visible;
        }
        catch
        {
            AssetGrid.FrozenColumnCount = 0;
        }
    }

    private void ApplySavedOrder()
    {
        if (DataContext is not InventoryViewModel vm || !vm.IsComputers) return;
        var order = vm.SavedColumnOrder;
        if (order is null || order.Count == 0) return;
        var max = Math.Max(0, AssetGrid.Columns.Count - 1);
        for (var i = 0; i < order.Count; i++)
        {
            var col = AssetGrid.Columns.FirstOrDefault(c =>
                string.Equals(c.Header?.ToString(), order[i], StringComparison.Ordinal));
            if (col is null) continue;
            try { col.DisplayIndex = Math.Clamp(i, 0, max); }
            catch (ArgumentException) { }
        }
    }

    private void ApplyDefaultOrder()
    {
        AssetGrid.FrozenColumnCount = 0;
        for (var i = 0; i < AssetGrid.Columns.Count; i++)
            AssetGrid.Columns[i].DisplayIndex = i;
        ApplyColumnVisibility();
        ApplyFreeze();
    }
}
