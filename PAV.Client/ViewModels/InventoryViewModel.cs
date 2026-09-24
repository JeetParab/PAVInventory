using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class InventoryViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly ShellViewModel _shell;
    private readonly ClientConfig _config;
    private readonly string _space;

    public bool IsComputers { get; }
    public bool ShowComputerTools => IsComputers;
    public string PageTitle => IsComputers ? "Inventory" : "Peripherals";
    public string PageHint => IsComputers
        ? "Laptops, desktops and all-in-ones"
        : "Monitors, printers, UPS, network devices and other kit";

    public ResetList<AssetListDto> Assets { get; } = [];
    public ObservableCollection<CategoryDto> Categories { get; } = [];
    public ObservableCollection<LocationDto> Locations { get; } = [];
    public ObservableCollection<UserDto> Users { get; } = [];
    public ObservableCollection<string> Manufacturers { get; } = [];

    public ICollectionView AssetsView { get; }

    [ObservableProperty] private AssetListDto? selected;
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string statusFilter = "All";
    [ObservableProperty] private int categoryFilter;
    [ObservableProperty] private int locationFilter;
    [ObservableProperty] private string manufacturerFilter = "All";
    [ObservableProperty] private string assignedFilter = "All";
    [ObservableProperty] private string purposeFilter = "Inventory";
    [ObservableProperty] private bool filtersOpen;
    [ObservableProperty] private bool toolsOpen;
    [ObservableProperty] private bool freezeIdentityColumns;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string emptyText = "No assets yet. Add an asset or import from Excel.";
    [ObservableProperty] private int visibleCount;
    [ObservableProperty] private bool canUndo;
    [ObservableProperty] private string undoLabel = "";

    private bool _suspendFilter;
    private readonly DispatcherTimer _searchDebounce;
    private string _searchNeedle = "";
    private int _matchCount;

    public List<string> StatusChoices { get; } = ["All", .. AssetStatusNames.All.Select(s => s.Display())];
    public List<string> AssignedChoices { get; } = ["All", "Assigned", "Unassigned"];
    public List<string> PurposeChoices { get; } = ["Inventory", "Temporary", "Pending confirm", "All"];

    public bool HasFilters =>
        !IsAll(StatusFilter) || CategoryFilter != 0 || LocationFilter != 0 ||
        !IsAll(ManufacturerFilter) || AssignedFilter is "Assigned" or "Unassigned" ||
        (IsComputers && PurposeFilter is "Temporary" or "Pending confirm" or "All") ||
        !string.IsNullOrWhiteSpace(SearchText);

    public int ActiveFilterCount =>
        (IsAll(StatusFilter) ? 0 : 1) +
        (CategoryFilter != 0 ? 1 : 0) +
        (LocationFilter != 0 ? 1 : 0) +
        (IsAll(ManufacturerFilter) ? 0 : 1) +
        (AssignedFilter is "Assigned" or "Unassigned" ? 1 : 0) +
        (IsComputers && PurposeFilter is "Temporary" or "Pending confirm" or "All" ? 1 : 0);

    public List<AssetListDto> SelectedAssets { get; private set; } = [];
    private SaveAssetRequest? _undoRequest;
    private DispatcherTimer? _undoTimer;

    public void SetSelected(IReadOnlyList<AssetListDto> items)
    {
        SelectedAssets = items.ToList();
        SelectedCount = items.Count;
        if (items.Count > 0)
            Selected = items[^1];
    }

    private List<int> SelectedIds() => SelectedAssets.Select(a => a.Id).ToList();

    public InventoryViewModel(ApiClient api, ShellViewModel shell, ClientConfig config, string space = "computers")
    {
        _api = api;
        _shell = shell;
        _config = config;
        _space = space;
        IsComputers = !string.Equals(space, "peripherals", StringComparison.OrdinalIgnoreCase);
        FreezeIdentityColumns = config.FreezeIdentityColumns;
        Loading = true;
        if (!IsComputers)
            PurposeFilter = "All";
        AssetsView = CollectionViewSource.GetDefaultView(Assets);
        AssetsView.Filter = FilterRow;
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            RefreshView();
        };
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_suspendFilter) return;
        _searchDebounce.Stop();
        if (string.IsNullOrWhiteSpace(value))
            RefreshView();
        else
            _searchDebounce.Start();
    }
    partial void OnStatusFilterChanged(string value) { if (!_suspendFilter) RefreshView(); }
    partial void OnCategoryFilterChanged(int value) { if (!_suspendFilter) RefreshView(); }
    partial void OnLocationFilterChanged(int value) { if (!_suspendFilter) RefreshView(); }
    partial void OnManufacturerFilterChanged(string value) { if (!_suspendFilter) RefreshView(); }
    partial void OnAssignedFilterChanged(string value) { if (!_suspendFilter) RefreshView(); }
    partial void OnPurposeFilterChanged(string value) { if (!_suspendFilter) RefreshView(); }

    private void RefreshView()
    {
        _searchNeedle = SearchText?.Trim() ?? "";
        _matchCount = 0;
        try { AssetsView.Refresh(); }
        catch { /* filter must not crash the grid */ }
        VisibleCount = _matchCount;
        EmptyText = Assets.Count == 0
            ? (IsComputers
                ? "No computers yet. Add an asset or import from Excel."
                : "No peripherals yet. Add a monitor, printer or other kit.")
            : "No assets match the current search or filters.";
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    public bool ShowEmpty => !Loading && VisibleCount == 0;

    private bool FilterRow(object obj)
    {
        try
        {
            if (obj is not AssetListDto a) return false;
            if (IsComputers)
            {
                if (a.CategoryFamily != CategoryFamily.Computer) return false;
            }
            else if (a.CategoryFamily != CategoryFamily.Peripheral) return false;
            if (PurposeFilter == "Inventory" && (a.IsTemporary || a.NeedsReview)) return false;
            if (PurposeFilter == "Temporary" && !a.IsTemporary) return false;
            if (PurposeFilter == "Pending confirm" && !a.NeedsReview) return false;
            if (!IsAll(StatusFilter) && a.Status != StatusFilter) return false;
            if (CategoryFilter != 0 && a.CategoryId != CategoryFilter) return false;
            if (LocationFilter != 0 && a.LocationId != LocationFilter) return false;
            if (!IsAll(ManufacturerFilter) &&
                !string.Equals(a.Manufacturer, ManufacturerFilter, StringComparison.OrdinalIgnoreCase))
                return false;
            if (AssignedFilter == "Assigned" && string.IsNullOrWhiteSpace(a.AssignedUser)) return false;
            if (AssignedFilter == "Unassigned" && !string.IsNullOrWhiteSpace(a.AssignedUser)) return false;
            var s = _searchNeedle ?? "";
            if (s.Length > 0)
            {
                if (!(Contains(a.AssetTag, s) ||
                      Contains(a.SerialNumber, s) ||
                      Contains(a.Hostname, s) ||
                      Contains(a.IpAddress, s) ||
                      Contains(a.AssignedUser, s) ||
                      (a.AssignedUserId is { } uid && Users.Any(u => u.Id == uid && Contains(u.SamAccount, s))) ||
                      Contains(a.Manufacturer, s) ||
                      Contains(a.Model, s) ||
                      Contains(a.MacAddress, s) ||
                      Contains(a.Designation, s) ||
                      Contains(a.Location, s) ||
                      Contains(a.Category, s) ||
                      Contains(a.Status, s) ||
                      Contains(a.MeLogon, s)))
                    return false;
            }
            _matchCount++;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAll(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("All", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrEmpty(value) &&
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    public async Task ApplyDashboardFilterAsync(string? status, int? categoryId)
    {
        SearchText = "";
        StatusFilter = status ?? "All";
        CategoryFilter = categoryId ?? 0;
        LocationFilter = 0;
        ManufacturerFilter = "All";
        AssignedFilter = "All";
        PurposeFilter = IsComputers ? "Inventory" : "All";
        await LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            using var _ = PAV.Core.Services.Perf.Measure("Inventory.Load");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var snap = await _api.InventoryLoadAsync();
            PAV.Core.Services.Perf.Log("Inventory.API", sw.ElapsedMilliseconds);

            var ids = CaptureSelection();
            var last = Selected?.Id;
            var family = IsComputers ? CategoryFamily.Computer : CategoryFamily.Peripheral;

            sw.Restart();
            _suspendFilter = true;
            Assets.ReplaceAll((snap.Assets ?? []).Where(a => a.CategoryFamily == family).ToList());

            Users.Clear();
            foreach (var u in (snap.Users ?? []).Where(u => u.IsActive))
                Users.Add(u);

            Categories.Clear();
            Categories.Add(new CategoryDto { Id = 0, Name = "All" });
            foreach (var c in (snap.Categories ?? []).Where(c => c.Family == family))
                Categories.Add(c);

            Locations.Clear();
            Locations.Add(new LocationDto { Id = 0, Name = "All" });
            foreach (var l in snap.Locations ?? [])
                Locations.Add(l);

            RebuildManufacturers();

            if (IsAll(StatusFilter)) StatusFilter = "All";
            if (CategoryFilter < 0) CategoryFilter = 0;
            if (LocationFilter < 0) LocationFilter = 0;
            if (IsAll(ManufacturerFilter)) ManufacturerFilter = "All";
            if (IsAll(AssignedFilter)) AssignedFilter = "All";
            _suspendFilter = false;

            RefreshView();
            RestoreSelection(ids, last);
            PAV.Core.Services.Perf.Log("Inventory.Bind", sw.ElapsedMilliseconds);
            PAV.Core.Services.Perf.Log("Inventory.ready", PAV.Core.Services.Perf.ElapsedMs);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            Loading = false;
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        StatusFilter = "All";
        CategoryFilter = 0;
        LocationFilter = 0;
        ManufacturerFilter = "All";
        AssignedFilter = "All";
        PurposeFilter = IsComputers ? "Inventory" : "All";
        FiltersOpen = false;
    }

    [RelayCommand]
    private void ToggleFilters() => FiltersOpen = !FiltersOpen;

    [RelayCommand]
    private void ToggleTools() => ToolsOpen = !ToolsOpen;

    [RelayCommand]
    private void ToggleFreezeColumns() => FreezeIdentityColumns = !FreezeIdentityColumns;

    public int FrozenColumnCount => IsComputers && FreezeIdentityColumns ? 6 : 0;

    public IReadOnlyList<string> SavedColumnOrder => _config.ColumnOrder;

    partial void OnFreezeIdentityColumnsChanged(bool value)
    {
        _config.FreezeIdentityColumns = value;
        _config.SaveUi();
        OnPropertyChanged(nameof(FrozenColumnCount));
        GridLayoutChanged?.Invoke();
    }

    public void SaveColumnOrder(IEnumerable<string> headers)
    {
        _config.ColumnOrder = headers.ToList();
        _config.SaveUi();
    }

    public event Action? ColumnOrderReset;
    public event Action? FocusSearchRequested;
    public event Action<IReadOnlyList<int>, int?>? RestoreSelectionRequested;
    public event Action? GridLayoutChanged;

    public void RequestFocusSearch() => FocusSearchRequested?.Invoke();

    [RelayCommand]
    private void FocusSearch() => RequestFocusSearch();

    public List<string> AssigneeNames()
    {
        return Users.Select(u => u.AssignLabel)
            .Concat(Assets.Select(a => a.AssignedUser))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .Cast<string>()
            .ToList();
    }

    private List<int> CaptureSelection() => SelectedAssets.Select(a => a.Id).ToList();

    private void RestoreSelection(List<int> ids, int? last)
    {
        var matches = Assets.Where(a => ids.Contains(a.Id)).ToList();
        SelectedAssets = matches;
        SelectedCount = matches.Count;
        Selected = last is { } id
            ? matches.FirstOrDefault(a => a.Id == id) ?? matches.LastOrDefault()
            : matches.LastOrDefault();
        RestoreSelectionRequested?.Invoke(ids, last);
    }

    public async Task ReloadAssetsAsync()
    {
        var ids = CaptureSelection();
        var last = Selected?.Id;
        try
        {
            using var _ = PAV.Core.Services.Perf.Measure("Inventory.Reload");
            var assets = await _api.AssetsAsync();
            Assets.ReplaceAll(assets);
            RebuildManufacturers();
            RefreshView();
            RestoreSelection(ids, last);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        OnPropertyChanged(nameof(ShowEmpty));
    }

    private void RebuildManufacturers()
    {
        var keep = ManufacturerFilter;
        Manufacturers.Clear();
        Manufacturers.Add("All");
        foreach (var m in Assets.Select(a => a.Manufacturer)
                     .Where(m => !string.IsNullOrWhiteSpace(m))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(m => m))
            Manufacturers.Add(m!);
        if (!string.IsNullOrWhiteSpace(keep) && Manufacturers.Contains(keep))
            ManufacturerFilter = keep;
        else if (IsAll(keep))
            ManufacturerFilter = "All";
    }

    private void ApplySaved(AssetListDto dto)
    {
        var ids = CaptureSelection();
        if (!ids.Contains(dto.Id))
            ids.Add(dto.Id);
        Assets.ReplaceOrAdd(dto, a => a.Id == dto.Id);
        RebuildManufacturers();
        RefreshView();
        RestoreSelection(ids, dto.Id);
        OnPropertyChanged(nameof(ShowEmpty));
    }

    private void ApplyRemoved(int id)
    {
        var ids = CaptureSelection().Where(x => x != id).ToList();
        var last = ids.LastOrDefault();
        Assets.RemoveWhere(a => a.Id == id);
        RebuildManufacturers();
        RefreshView();
        RestoreSelection(ids, last == 0 ? null : last);
        OnPropertyChanged(nameof(ShowEmpty));
    }

    private void PatchByIds(IReadOnlyCollection<int> ids, Action<AssetListDto> mutate)
    {
        var set = ids as HashSet<int> ?? ids.ToHashSet();
        if (set.Count == 0) return;
        var captured = CaptureSelection();
        var last = Selected?.Id;
        var next = new List<AssetListDto>(Assets.Count);
        foreach (var a in Assets)
        {
            if (set.Contains(a.Id))
            {
                var clone = a.Clone();
                mutate(clone);
                clone.Version++;
                next.Add(clone);
            }
            else
            {
                next.Add(a);
            }
        }
        Assets.ReplaceAll(next);
        RefreshView();
        RestoreSelection(captured, last);
        OnPropertyChanged(nameof(ShowEmpty));
    }

    [RelayCommand]
    private void ResetColumns()
    {
        ToolsOpen = false;
        _config.ColumnOrder = [];
        _config.SaveUi();
        ColumnOrderReset?.Invoke();
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!_shell.CanAdd) return;
        var vm = new AssetEditViewModel(_api, Categories.Where(c => c.Id != 0).ToList(),
            Locations.Where(l => l.Id != 0).ToList(), Users.ToList(), AssigneeNames(), null,
            computerFields: IsComputers);
        var win = new AssetEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            if (vm.SavedAsset is not null)
                ApplySaved(vm.SavedAsset);
            else
                await ReloadAssetsAsync();
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null || !_shell.CanEdit) return;
        await OpenEdit(Selected.Id);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (Selected is null) return;
        try
        {
            var detail = await _api.AssetAsync(Selected.Id);
            var vm = new AssetDetailViewModel(_api, _shell, detail);
            var win = new AssetDetailWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            win.ShowDialog();
            if (vm.OpenEditor)
                await OpenEdit(vm.Asset.Id, vm.Asset);
            else if (vm.Changed)
                await ReloadAssetsAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    public async Task OpenEdit(int id, AssetListDto? preloaded = null)
    {
        try
        {
            var detail = preloaded ?? await _api.AssetAsync(id, history: false);
            var vm = new AssetEditViewModel(_api,
                Categories.Where(c => c.Id != 0).ToList(),
                Locations.Where(l => l.Id != 0).ToList(),
                Users.ToList(),
                AssigneeNames(),
                detail,
                computerFields: IsComputers);
            var win = new AssetEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            if (win.ShowDialog() == true)
            {
                if (vm.SavedAsset is not null)
                    ApplySaved(vm.SavedAsset);
                else
                    await ReloadAssetsAsync();
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task CopyAsNewAsync()
    {
        if (Selected is null || !_shell.CanAdd) return;
        var vm = new AssetEditViewModel(_api,
            Categories.Where(c => c.Id != 0).ToList(),
            Locations.Where(l => l.Id != 0).ToList(),
            Users.ToList(),
            AssigneeNames(),
            Selected,
            copy: true,
            computerFields: IsComputers);
        var win = new AssetEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            if (vm.SavedAsset is not null)
                ApplySaved(vm.SavedAsset);
            else
                await ReloadAssetsAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null || !_shell.CanDelete) return;
        if (!Ui.Confirm($"Delete asset {Selected.AssetTag}?\n\nYou can undo this from the bar at the bottom."))
            return;
        try
        {
            var snapshot = Selected.Clone();
            await _api.DeleteAssetAsync(snapshot.Id);
            ArmUndo(snapshot);
            ApplyRemoved(snapshot.Id);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task UndoDeleteAsync()
    {
        if (_undoRequest is null) return;
        try
        {
            var created = await _api.CreateAssetAsync(_undoRequest);
            ClearUndo();
            ApplySaved(created);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    private void ArmUndo(AssetListDto a)
    {
        _undoRequest = ToSave(a);
        CanUndo = true;
        UndoLabel = $"Deleted {a.AssetTag}";
        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _undoTimer.Tick += (_, _) => ClearUndo();
        _undoTimer.Start();
    }

    private void ClearUndo()
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        _undoRequest = null;
        CanUndo = false;
        UndoLabel = "";
    }

    private static SaveAssetRequest ToSave(AssetListDto a) => new()
    {
        AssetTag = a.AssetTag,
        SrNo = a.SrNo,
        CategoryId = a.CategoryId,
        Manufacturer = a.Manufacturer,
        Model = a.Model,
        SerialNumber = a.SerialNumber,
        Hostname = a.Hostname,
        IpAddress = a.IpAddress,
        LocationId = a.LocationId,
        Status = a.StatusValue,
        AssignedUserId = a.AssignedUserId,
        AssignedUserName = a.AssignedUser,
        Designation = a.Designation,
        AlternateUser = a.AlternateUser,
        Domain = a.Domain,
        MacAddress = a.MacAddress,
        Processor = a.Processor,
        Ram = a.Ram,
        Storage = a.Storage,
        OperatingSystem = a.OperatingSystem,
        DcInstalled = a.DcInstalled,
        AvInstalled = a.AvInstalled,
        MsOfficeVersion = a.MsOfficeVersion,
        MfaEnabled = a.MfaEnabled,
        IvantiInstalled = a.IvantiInstalled,
        AdminRights = a.AdminRights,
        UsbAccess = a.UsbAccess,
        ChromeUpdated = a.ChromeUpdated,
        StockAvailability = a.StockAvailability,
        StockWorking = a.StockWorking,
        PmCompleted = a.PmCompleted,
        LastConnected = a.LastConnected,
        CollectBy = a.CollectBy,
        PurchaseDate = a.PurchaseDate,
        WarrantyExpiry = a.WarrantyExpiry,
        Remarks = a.Remarks
    };

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!_shell.CanExport) return;
        var path = Ui.SaveExcel($"PAV-Inventory_{DateTime.Now:yyyy-MM-dd}.xlsx");
        if (path is null) return;
        try
        {
            var q = ApiClient.Query(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                StatusFilter == "All" ? null : StatusFilter,
                CategoryFilter == 0 ? null : CategoryFilter,
                LocationFilter == 0 ? null : LocationFilter,
                ManufacturerFilter == "All" ? null : ManufacturerFilter,
                AssignedFilter == "Assigned" ? true : AssignedFilter == "Unassigned" ? false : null);
            var bytes = await _api.ExportAsync(q);
            await Ui.SaveBytes(path, bytes);
            Ui.Info("Inventory exported.");
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            ToolsOpen = false;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!_shell.CanImport) return;
        ToolsOpen = false;
        var win = new ImportWizardWindow
        {
            DataContext = new ImportWizardViewModel(_api),
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (win.ShowDialog() == true)
            await ReloadAssetsAsync();
    }

    [RelayCommand]
    private async Task ImportManageEngineAsync()
    {
        if (!_shell.CanImport) return;
        ToolsOpen = false;
        var path = Ui.OpenExcel();
        if (path is null) return;
        try
        {
            var preview = await _api.PreviewMeImportAsync(path);
            var vm = new MeImportViewModel(_api, path, preview);
            var win = new MeImportWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            if (win.ShowDialog() == true)
            {
                if (!string.IsNullOrWhiteSpace(vm.ResultSummary))
                    Ui.Info(vm.ResultSummary);
                await ReloadAssetsAsync();
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task TemplateAsync()
    {
        if (!_shell.CanImport) return;
        var path = Ui.SaveExcel("PAV-Import-Template.xlsx");
        if (path is null) return;
        try
        {
            var bytes = await _api.ImportTemplateAsync();
            await File.WriteAllBytesAsync(path, bytes);
            Ui.Info("Import template saved.");
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            ToolsOpen = false;
        }
    }

    [RelayCommand]
    private async Task BulkEditAsync()
    {
        if (!_shell.CanEdit) return;
        ToolsOpen = false;
        if (SelectedAssets.Count == 0)
        {
            Ui.Info("Select one or more rows first.");
            return;
        }
        var vm = new BulkEditViewModel(_api, SelectedIds(), Locations.Where(l => l.Id > 0).ToList(),
            Users.ToList(), AssigneeNames());
        var win = new BulkEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
            await ReloadAssetsAsync();
    }

    [RelayCommand]
    private async Task BulkAddAsync()
    {
        if (!_shell.CanAdd) return;
        ToolsOpen = false;
        var vm = new BulkAddViewModel(_api,
            Categories.Where(c => c.Id != 0).ToList(),
            Locations.Where(l => l.Id != 0).ToList());
        var win = new BulkAddWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
            await ReloadAssetsAsync();
    }

    [RelayCommand]
    private async Task BulkStatusAsync()
    {
        if (!_shell.CanEdit) return;
        ToolsOpen = false;
        if (SelectedAssets.Count == 0)
        {
            Ui.Info("Select one or more rows first.");
            return;
        }
        var win = new BulkStatusWindow(SelectedAssets.Count) { Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() != true || string.IsNullOrWhiteSpace(win.ChosenStatus)) return;
        if (!AssetStatusNames.TryParse(win.ChosenStatus, out var st)) return;
        try
        {
            var ids = SelectedIds();
            var n = await _api.BulkPatchAsync(new BulkEditRequest { Ids = ids, Status = st });
            Ui.Info($"Status set on {n} assets.");
            PatchByIds(ids, a =>
            {
                a.StatusValue = st;
                a.Status = st.Display();
            });
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task MarkConnectedTodayAsync()
    {
        if (!_shell.CanEdit) return;
        ToolsOpen = false;
        if (SelectedAssets.Count == 0)
        {
            Ui.Info("Select one or more rows first.");
            return;
        }
        if (!Ui.Confirm($"Set Last Connected to today on {SelectedAssets.Count} selected assets?"))
            return;
        try
        {
            var ids = SelectedIds();
            var n = await _api.BulkPatchAsync(new BulkEditRequest
            {
                Ids = ids,
                SetLastConnected = true,
                LastConnected = DateTime.Today
            });
            Ui.Info($"Last Connected updated on {n} assets.");
            PatchByIds(ids, a => a.LastConnected = DateTime.Today);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task RenumberByIpAsync()
    {
        if (!_shell.CanEdit) return;
        ToolsOpen = false;
        var ids = SelectedIds();
        var scope = ids.Count == 0 ? "all assets" : $"{ids.Count} selected assets";
        if (!Ui.Confirm($"Renumber Sr No by IP order on {scope}?\n\nWIFI / blank IPs go last."))
            return;
        try
        {
            var n = await _api.RenumberByIpAsync(ids.Count == 0 ? null : ids);
            Ui.Info($"Sr No updated on {n} assets.");
            await ReloadAssetsAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task FindDuplicatesAsync()
    {
        ToolsOpen = false;
        try
        {
            var groups = await _api.DuplicateSerialsAsync();
            if (groups.Count == 0)
            {
                Ui.Info("No duplicate serial numbers.");
                return;
            }
            var vm = new DuplicatesViewModel(_api, groups);
            var win = new DuplicatesWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            win.ShowDialog();
            if (vm.Changed)
                await ReloadAssetsAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task ExportViewAsync()
    {
        if (!_shell.CanExport) return;
        ToolsOpen = false;
        var rows = AssetsView.Cast<AssetListDto>().ToList();
        var path = Ui.SaveCsv($"PAV-View_{DateTime.Now:yyyy-MM-dd}.csv");
        if (path is null) return;
        try
        {
            await File.WriteAllTextAsync(path, ToCsv(rows));
            Ui.Info($"Exported {rows.Count} rows from the current view.");
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task AddLocationAsync()
    {
        if (!_shell.CanManageLocations)
        {
            Ui.Info("Only an administrator can add locations.");
            return;
        }
        ToolsOpen = false;
        var win = new AddLocationWindow { Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() != true) return;
        try
        {
            await _api.CreateLocationAsync(new SaveLocationRequest
            {
                Name = win.LocationName,
                Description = win.Description
            });
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private void Assign()
    {
        if (!_shell.CanEdit) return;
        ToolsOpen = false;
        if (SelectedAssets.Count == 0)
        {
            Ui.Info("Select one or more rows first.");
            return;
        }
        var vm = new QuickAssignViewModel(_api, SelectedIds(), Users.ToList(), AssigneeNames());
        var win = new QuickAssignWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            PatchByIds(SelectedIds(), a =>
            {
                a.AssignedUserId = vm.AppliedUserId;
                a.AssignedUser = vm.AppliedName;
            });
        }
    }

    [RelayCommand]
    private async Task UnassignAsync()
    {
        if (!_shell.CanEdit) return;
        if (SelectedAssets.Count == 0)
        {
            Ui.Info("Select one or more rows first.");
            return;
        }
        if (!Ui.Confirm($"Unassign {SelectedAssets.Count} selected asset(s)?"))
            return;
        try
        {
            var ids = SelectedIds();
            var n = await _api.BulkPatchAsync(new BulkEditRequest
            {
                Ids = ids,
                SetAssignedUser = true,
                AssignedUserId = null,
                AssignedUserName = null
            });
            Ui.Info($"Unassigned {n} assets.");
            PatchByIds(ids, a =>
            {
                a.AssignedUserId = null;
                a.AssignedUser = null;
            });
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    private static string ToCsv(List<AssetListDto> rows)
    {
        var headers = new[]
        {
            "Sr No", "Location", "Asset_Category", "Make_Model", "Serial_Number", "Hostname", "IP_Address",
            "User Name", "Designation", "Status", "Domain", "MAC_Address", "Processor", "RAM", "Storage", "OS",
            "DC_Installed", "AV_Installed", "MS_Office_Version", "MFA_Enabled", "Ivanti_Installed",
            "Admin_Rights", "USB_Access", "Chrome_Updated", "Stock_Availability", "Stock_Working",
            "PM_Completed", "Alternate_User", "Last Connected", "Collect By"
        };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Csv)));
        foreach (var a in rows)
        {
            string[] cells =
            [
                a.SrNo?.ToString() ?? "",
                a.Location ?? "",
                a.Category,
                a.MakeModel,
                a.SerialNumber ?? "",
                a.Hostname ?? "",
                a.IpAddress ?? "",
                a.AssignedUser ?? "",
                a.Designation ?? "",
                a.Status,
                a.Domain ?? "",
                a.MacAddress ?? "",
                a.Processor ?? "",
                a.Ram ?? "",
                a.Storage ?? "",
                a.OperatingSystem ?? "",
                a.DcInstalled ?? "",
                a.AvInstalled ?? "",
                a.MsOfficeVersion ?? "",
                a.MfaEnabled ?? "",
                a.IvantiInstalled ?? "",
                a.AdminRights ?? "",
                a.UsbAccess ?? "",
                a.ChromeUpdated ?? "",
                a.StockAvailability ?? "",
                a.StockWorking ?? "",
                a.PmCompleted ?? "",
                a.AlternateUser ?? "",
                a.LastConnected?.ToString("yyyy-MM-dd") ?? "",
                a.CollectBy ?? ""
            ];
            sb.AppendLine(string.Join(",", cells.Select(Csv)));
        }
        return sb.ToString();
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
