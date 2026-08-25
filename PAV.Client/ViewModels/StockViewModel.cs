using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class StockViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly ShellViewModel _shell;

    private readonly string _scope;
    private readonly List<StockItemDto> _allItems = [];
    private readonly List<StockMovementDto> _allMoves = [];
    private bool _loadedMoves;
    private int? _loadedMoveItemId;


    public ObservableCollection<StockItemDto> Items { get; } = [];
    public ObservableCollection<StockMovementDto> Movements { get; } = [];
    public ObservableCollection<string> StatusChoices { get; } = ["All", "In Stock", "Low Stock", "Out of Stock", "Review"];

    public string Title { get; }
    public string DefaultCategory { get; }
    public bool IsToner => _scope == "toner";

    [ObservableProperty] private string page = "Stock";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string statusFilter = "All";
    [ObservableProperty] private StockItemDto? selected;
    [ObservableProperty] private StockMovementDto? selectedMovement;
    [ObservableProperty] private int itemCount;
    [ObservableProperty] private int onHandUnits;
    [ObservableProperty] private int lowStock;
    [ObservableProperty] private int outOfStock;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string? message;

    public bool ShowStock => Page == "Stock";
    public bool ShowMovements => Page == "Movements";
    public bool CanMove => _shell.Can(Permissions.Assign);
    public bool CanManage => _shell.Me?.Role == UserRole.Administrator;
    public bool CanExport => _shell.CanExport;

    public StockViewModel(ApiClient api, ShellViewModel shell, string scope = "stock")
    {
        _api = api;
        _shell = shell;
        _scope = string.IsNullOrWhiteSpace(scope) ? "stock" : scope.Trim().ToLowerInvariant();
        Title = _scope == "toner" ? "Toner" : "Stock";
        DefaultCategory = _scope == "toner" ? "Toner" : "Peripheral";
    }

    partial void OnPageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowStock));
        OnPropertyChanged(nameof(ShowMovements));
        if (value == "Movements")
            _ = ReloadMovementsAsync();
        else
            _ = ReloadItemMovementsAsync();
    }

    [RelayCommand]
    private void ShowPage(string p) => Page = p;

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            var ov = await _api.StockOverviewAsync(search: null, status: "All", includeInactive: false, categoryScope: _scope);
            _allItems.Clear();
            _allItems.AddRange(ov.Items);
            _loadedMoves = false;
            _allMoves.Clear();
            ApplyFilter();
            if (ShowMovements)
                await EnsureMovementsAsync();
            else
                await ReloadItemMovementsAsync();
            OnPropertyChanged(nameof(CanMove));
            OnPropertyChanged(nameof(CanManage));
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            Loading = false;
        }
    }

    partial void OnSearchChanged(string value)
    {
        ApplyFilter();
        if (ShowMovements)
            ApplyMoveFilter();
    }

    partial void OnStatusFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedChanged(StockItemDto? value)
    {
        if (value?.Id == _loadedMoveItemId) return;
        _ = ReloadItemMovementsAsync();
    }


    private void ApplyFilter()
    {
        var keep = Selected?.Id;
        var q = _allItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            q = q.Where(i =>
                Contains(i.Name, s) ||
                Contains(i.Manufacturer, s) ||
                Contains(i.Model, s) ||
                Contains(i.Category, s) ||
                Contains(i.Notes, s));
        }

        q = (StatusFilter ?? "all").Trim().ToLowerInvariant() switch

        {
            "low" or "low stock" => q.Where(x => x.IsLow),
            "out" or "out of stock" => q.Where(x => x.OnHand <= 0),
            "in" or "in stock" => q.Where(x => x.StockStatus == "In Stock"),
            "review" => q.Where(x => x.NeedsReview),
            _ => q
        };

        var list = q.ToList();
        ItemCount = list.Count;
        OnHandUnits = list.Where(x => x.IsActive).Sum(x => x.OnHand);
        LowStock = list.Count(x => x.IsLow && x.IsActive);
        OutOfStock = list.Count(x => x.OnHand <= 0 && x.IsActive);
        Items.Clear();
        foreach (var i in list)
            Items.Add(i);
        Selected = Items.FirstOrDefault(x => x.Id == keep) ?? Items.FirstOrDefault();
    }

    private static bool Contains(string? value, string s) =>
        !string.IsNullOrEmpty(value) && value.Contains(s, StringComparison.OrdinalIgnoreCase);

    private async Task ReloadItemMovementsAsync()
    {
        if (ShowMovements) return;
        try
        {
            if (Selected is null)
            {
                _loadedMoveItemId = null;
                Movements.Clear();
                return;
            }
            _loadedMoveItemId = Selected.Id;
            var list = await _api.StockMovementsAsync(Selected.Id);

            Movements.Clear();
            foreach (var m in list.Take(40))
                Movements.Add(m);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    private async Task EnsureMovementsAsync()
    {
        if (!_loadedMoves)
        {
            var list = await _api.StockMovementsAsync(categoryScope: _scope);
            _allMoves.Clear();
            _allMoves.AddRange(list);
            _loadedMoves = true;
        }
        ApplyMoveFilter();
    }

    private void ApplyMoveFilter()
    {
        var q = _allMoves.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            q = q.Where(m =>
                Contains(m.ItemName, s) ||
                Contains(m.AssignedUser, s) ||
                Contains(m.Notes, s) ||
                Contains(m.Reference, s) ||
                Contains(m.SerialNumber, s));
        }
        Movements.Clear();
        foreach (var m in q)
            Movements.Add(m);
    }

    private async Task ReloadMovementsAsync() => await EnsureMovementsAsync();

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private async Task AddItemAsync()
    {
        if (!CanManage) return;
        await OpenItemEditor(null);
    }

    [RelayCommand]
    private async Task EditItemAsync()
    {
        if (!CanManage || Selected is null) return;
        await OpenItemEditor(Selected);
    }

    private async Task OpenItemEditor(StockItemDto? existing)
    {
        var vm = new StockItemEditViewModel(_api, existing, DefaultCategory);

        var win = new StockItemEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
            await LoadAsync();
    }

    [RelayCommand]
    private Task ReceiveAsync() => OpenMove("Receive");

    [RelayCommand]
    private Task IssueAsync() => OpenMove("Issue");

    [RelayCommand]
    private Task ReturnAsync() => OpenMove("Return");

    [RelayCommand]
    private Task AdjustAsync() => OpenMove("Adjust");

    private async Task OpenMove(string kind)
    {
        if (kind == "Adjust" && !CanManage) return;
        if (kind != "Adjust" && !CanMove) return;
        if (Items.Count == 0)
        {
            Ui.Info(_scope == "toner"
                ? "No toner items yet. Import the cleaned 2025 file, or add a toner row."
                : "Add a stock item first (or import the 2026 Excel).");
            return;
        }
        var vm = new StockMoveViewModel(_api, kind, Items.ToList(), Selected);
        var win = new StockMoveWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
            await LoadAsync();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!CanManage) return;
        var path = Ui.OpenExcel();
        if (path is null) return;
        try
        {
            Loading = true;
            var preview = await _api.PreviewStockImportAsync(path);
            var vm = new StockImportViewModel(_api, path, preview);
            var win = new StockImportWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            if (win.ShowDialog() == true)
            {
                Message = vm.ResultSummary;
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            Loading = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!CanExport) return;
        var path = Ui.SaveExcel("PAV-stock.xlsx");
        if (path is null) return;
        try
        {
            var bytes = await _api.ExportStockAsync();
            await Ui.SaveBytes(path, bytes);
            Ui.Info("Exported stock and movements.");
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task IntegrityAsync()
    {
        if (!CanManage) return;
        try
        {
            var report = await _api.StockIntegrityAsync();
            if (report.MismatchCount == 0)
            {
                Ui.Info(report.Summary);
                return;
            }

            var lines = report.Rows
                .Where(r => r.Status == "MISMATCH")
                .Select(r => $"{r.Name}: stored {r.StoredOnHand}, ledger {r.CalculatedOnHand}");
            Ui.Info(report.Summary + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }
}
