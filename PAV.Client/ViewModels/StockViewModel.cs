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

    public ObservableCollection<StockItemDto> Items { get; } = [];
    public ObservableCollection<StockMovementDto> Movements { get; } = [];
    public ObservableCollection<string> StatusChoices { get; } = ["All", "In Stock", "Low Stock", "Out of Stock", "Review"];

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

    public StockViewModel(ApiClient api, ShellViewModel shell)
    {
        _api = api;
        _shell = shell;
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
            var ov = await _api.StockOverviewAsync(Search, StatusFilter, includeInactive: false);
            ItemCount = ov.ItemCount;
            OnHandUnits = ov.OnHandUnits;
            LowStock = ov.LowStock;
            OutOfStock = ov.OutOfStock;
            var keep = Selected?.Id;
            Items.Clear();
            foreach (var i in ov.Items)
                Items.Add(i);
            Selected = Items.FirstOrDefault(x => x.Id == keep) ?? Items.FirstOrDefault();
            if (ShowMovements)
                await ReloadMovementsAsync();
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

    partial void OnSearchChanged(string value) => _ = DebouncedLoad();
    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();
    partial void OnSelectedChanged(StockItemDto? value) => _ = ReloadItemMovementsAsync();

    private async Task DebouncedLoad()
    {
        var token = Search;
        await Task.Delay(180);
        if (token != Search) return;
        await LoadAsync();
    }

    private async Task ReloadItemMovementsAsync()
    {
        if (ShowMovements) return;
        try
        {
            if (Selected is null)
            {
                Movements.Clear();
                return;
            }
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

    private async Task ReloadMovementsAsync()
    {
        try
        {
            var list = await _api.StockMovementsAsync(search: Search);
            Movements.Clear();
            foreach (var m in list)
                Movements.Add(m);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

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
        var vm = new StockItemEditViewModel(_api, existing);
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
            Ui.Info("Add a stock item first (or import the 2026 Excel).");
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
}
