using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class FloorCard : ObservableObject
{
    public required IpFloorSummaryDto Data { get; init; }
    [ObservableProperty] private bool isSelected;
}

public class FloorFilterItem
{
    public int? RangeId { get; init; }
    public string Name { get; init; } = "";
    public override string ToString() => Name;
}

public partial class IpInventoryViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly ShellViewModel _shell;
    private bool _syncingFloor;

    public ObservableCollection<FloorCard> Floors { get; } = [];
    public ObservableCollection<FloorFilterItem> FloorFilters { get; } = [];
    public ObservableCollection<IpAddressDto> Rows { get; } = [];
    public ObservableCollection<string> StatusChoices { get; } = ["Allocated", "Used", "Reserved", "Free", "All"];

    [ObservableProperty] private string ipPage = "Assign";
    [ObservableProperty] private int total;
    [ObservableProperty] private int used;
    [ObservableProperty] private int free;
    [ObservableProperty] private int reserved;
    [ObservableProperty] private int? selectedRangeId;
    [ObservableProperty] private FloorFilterItem? selectedListFloor;
    [ObservableProperty] private string selectedFloorName = "All floors";
    [ObservableProperty] private string? nextFreeIp;
    [ObservableProperty] private string checkInput = "";
    [ObservableProperty] private IpCheckResultDto? checkResult;
    [ObservableProperty] private string statusFilter = "Allocated";
    [ObservableProperty] private string listSearch = "";
    [ObservableProperty] private IpAddressDto? selected;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string? message;

    public bool ShowAssign => IpPage == "Assign";
    public bool ShowList => IpPage == "List";
    public bool CanAssign => _shell.Can(Permissions.Assign);
    public bool CanImport => _shell.CanImport;
    public bool HasData => Floors.Count > 0;
    public bool CheckIsFree => CheckResult?.Record?.StatusValue == IpStatus.Free;
    public bool CheckIsTaken => CheckResult is { InPool: true, Record: not null } &&
                                CheckResult.Record.StatusValue is IpStatus.Used or IpStatus.Reserved
                                    or IpStatus.Quarantine or IpStatus.Deprecated;
    public int VisibleCount => Rows.Count;

    public IpInventoryViewModel(ApiClient api, ShellViewModel shell)
    {
        _api = api;
        _shell = shell;
    }

    partial void OnIpPageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowAssign));
        OnPropertyChanged(nameof(ShowList));
    }

    [RelayCommand]
    private void ShowPage(string page) => IpPage = page;

    public async Task LoadAsync()
    {
        Loading = true;
        Message = null;
        try
        {
            var overview = await _api.IpOverviewAsync();
            Total = overview.Total;
            Used = overview.Used;
            Free = overview.Free;
            Reserved = overview.Reserved;

            var keep = SelectedRangeId;
            Floors.Clear();
            foreach (var f in overview.Floors)
            {
                Floors.Add(new FloorCard
                {
                    Data = f,
                    IsSelected = keep is { } id && id == f.RangeId
                });
            }

            _syncingFloor = true;
            FloorFilters.Clear();
            FloorFilters.Add(new FloorFilterItem { RangeId = null, Name = "All floors" });
            foreach (var f in overview.Floors)
                FloorFilters.Add(new FloorFilterItem { RangeId = f.RangeId, Name = f.Name });
            SelectedListFloor = FloorFilters.FirstOrDefault(x => x.RangeId == keep) ?? FloorFilters[0];
            _syncingFloor = false;

            if (keep is { } kid && Floors.All(x => x.Data.RangeId != kid))
                SelectedRangeId = null;

            ApplyFloorLabel();
            await ReloadListAsync();
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(CanAssign));
            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(VisibleCount));
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

    private void ApplyFloorLabel()
    {
        var card = Floors.FirstOrDefault(x => x.IsSelected);
        if (card is null)
        {
            SelectedFloorName = "All floors";
            NextFreeIp = Floors.Select(x => x.Data.NextFreeIp).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            return;
        }
        SelectedFloorName = card.Data.Name;
        NextFreeIp = card.Data.NextFreeIp;
    }

    private async Task ReloadListAsync()
    {
        var status = StatusFilter.ToLowerInvariant() switch
        {
            "used" => "used",
            "reserved" => "reserved",
            "free" => "free",
            "all" => "all",
            _ => "allocated"
        };
        var list = await _api.IpListAsync(SelectedRangeId, status, ListSearch);
        Rows.Clear();
        foreach (var row in list)
            Rows.Add(row);
        OnPropertyChanged(nameof(VisibleCount));
    }

    [RelayCommand]
    private void SelectFloor(FloorCard? card)
    {
        if (card is null) return;
        if (card.IsSelected)
            SelectedListFloor = FloorFilters.FirstOrDefault();
        else
            SelectedListFloor = FloorFilters.FirstOrDefault(x => x.RangeId == card.Data.RangeId);
    }

    partial void OnSelectedListFloorChanged(FloorFilterItem? value)
    {
        if (_syncingFloor) return;
        SelectedRangeId = value?.RangeId;
        foreach (var f in Floors)
            f.IsSelected = value?.RangeId is { } id && f.Data.RangeId == id;
        ApplyFloorLabel();
        _ = SafeReload();
    }

    partial void OnStatusFilterChanged(string value) => _ = SafeReload();
    partial void OnListSearchChanged(string value) => _ = SafeReload();

    private async Task SafeReload()
    {
        try { await ReloadListAsync(); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        try
        {
            CheckResult = await _api.IpCheckAsync(CheckInput);
            OnPropertyChanged(nameof(CheckIsFree));
            OnPropertyChanged(nameof(CheckIsTaken));
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task AssignNextFreeAsync()
    {
        if (!CanAssign) return;
        var rangeId = SelectedRangeId ?? Floors.FirstOrDefault()?.Data.RangeId;
        var next = SelectedRangeId is null
            ? Floors.FirstOrDefault()?.Data.NextFreeIp
            : NextFreeIp;
        if (rangeId is null || string.IsNullOrWhiteSpace(next))
        {
            Ui.Info("No free IP on the selected floor.");
            return;
        }
        await OpenAssignAsync(null, rangeId, next);
    }

    [RelayCommand]
    private async Task AssignCustomAsync()
    {
        if (!CanAssign) return;
        var typed = CheckInput?.Trim();
        await OpenAssignAsync(null, null, string.IsNullOrWhiteSpace(typed) ? null : typed);
    }

    [RelayCommand]
    private async Task AssignCheckedAsync()
    {
        if (!CanAssign) return;
        if (CheckResult?.Record is not null)
        {
            await OpenAssignAsync(CheckResult.Record, null, CheckResult.Record.Address);
            return;
        }
        await AssignCustomAsync();
    }

    [RelayCommand]
    private async Task AssignSelectedAsync()
    {
        if (!CanAssign || Selected is null) return;
        if (Selected.StatusValue != IpStatus.Free)
        {
            Ui.Info($"{Selected.Address} is {Selected.Status}.");
            return;
        }
        await OpenAssignAsync(Selected, null, Selected.Address);
    }

    [RelayCommand]
    private async Task OpenRowAsync()
    {
        if (Selected is null) return;
        if (Selected.StatusValue == IpStatus.Free)
            await AssignSelectedAsync();
        else
            IpPage = "Assign";
    }

    private async Task OpenAssignAsync(IpAddressDto? existing, int? rangeId, string? address)
    {
        var vm = new IpAssignViewModel(_api, existing, rangeId, address);
        var win = new IpAssignWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            await LoadAsync();
            if (!string.IsNullOrWhiteSpace(address))
                CheckInput = address;
            if (!string.IsNullOrWhiteSpace(CheckInput))
                await CheckAsync();
        }
    }

    [RelayCommand]
    private async Task ReleaseAsync()
    {
        var rec = Selected ?? CheckResult?.Record;
        if (!CanAssign || rec is null) return;
        if (rec.StatusValue == IpStatus.Free)
        {
            Ui.Info($"{rec.Address} is already free.");
            return;
        }
        if (!Ui.Confirm($"Release {rec.Address}? It will go back to Free."))
            return;
        try
        {
            await _api.IpReleaseAsync(rec.Id);
            await LoadAsync();
            CheckInput = rec.Address;
            await CheckAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task ReserveSelectedAsync()
    {
        if (!CanAssign || Selected is null) return;
        try
        {
            await _api.IpReserveAsync(Selected.Id, Selected.Notes);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task ImportWorkbookAsync()
    {
        if (!CanImport) return;
        var path = Ui.OpenExcel();
        if (path is null) return;
        if (!Ui.Confirm("Import Floors_Config and IP_Inventory from this workbook? Existing IPs with the same address will be updated."))
            return;
        try
        {
            Loading = true;
            var result = await _api.ImportIpWorkbookAsync(path);
            Message = result.Summary;
            if (result.Errors.Count > 0)
                Ui.Error(new ApiException(400, "validation", result.Summary, result.Errors.Take(12).ToList()));
            else
                Ui.Info(result.Summary);
            await LoadAsync();
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
    private Task RefreshAsync() => LoadAsync();
}
