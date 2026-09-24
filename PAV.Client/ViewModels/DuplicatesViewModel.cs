using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class DuplicateRow : ObservableObject
{
    public required DuplicateGroupDto Group { get; init; }
    [ObservableProperty] private int keepId;
}

public partial class DuplicatesViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<DuplicateRow> Groups { get; } = [];
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool busy;
    public event Action<bool>? CloseRequested;
    public bool Changed { get; private set; }

    public DuplicatesViewModel(ApiClient api, List<DuplicateGroupDto> groups)
    {
        _api = api;
        foreach (var g in groups)
        {
            Groups.Add(new DuplicateRow
            {
                Group = g,
                KeepId = g.Assets.OrderBy(a => a.SrNo ?? int.MaxValue).First().Id
            });
        }
    }

    [RelayCommand]
    private async Task RemoveExtrasAsync()
    {
        if (!Ui.Confirm("Delete the extra copies and keep the selected row in each group?"))
            return;
        Busy = true;
        Error = null;
        try
        {
            var n = 0;
            foreach (var row in Groups)
            {
                foreach (var a in row.Group.Assets.Where(a => a.Id != row.KeepId))
                {
                    await _api.DeleteAssetAsync(a.Id);
                    n++;
                }
            }
            Changed = n > 0;
            Ui.Info(n == 0 ? "Nothing to delete." : $"Removed {n} duplicate rows.");
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(Changed);
}
