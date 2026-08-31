using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class PendingViewModel(ApiClient api, ShellViewModel shell) : ObservableObject
{
    public ObservableCollection<PendingDetailDto> Rows { get; } = [];

    [ObservableProperty] private PendingDetailDto? selected;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string summary = "";

    public bool CanComplete => shell.CanEdit || shell.CanAdd;

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            var list = await api.PendingAsync();
            Rows.Clear();
            foreach (var row in list) Rows.Add(row);
            Summary = list.Count == 0
                ? "Nothing pending. Floor IPs and inventory agree on user, hostname and MAC, and kit details are filled."
                : $"{list.Count} item(s) need attention — missing details, IP/inventory mismatch, or the same IP on two assets.";
            OnPropertyChanged(nameof(CanComplete));
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
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task CompleteAsync()
    {
        if (Selected is null || !CanComplete) return;
        await OpenAsync(Selected);
    }

    [RelayCommand]
    private async Task OpenRowAsync()
    {
        if (Selected is null) return;
        await OpenAsync(Selected);
    }

    private async Task OpenAsync(PendingDetailDto row)
    {
        try
        {
            var load = await api.InventoryLoadAsync();
            var cats = load.Categories.Where(c => c.Id != 0).ToList();
            var locs = load.Locations.Where(l => l.Id != 0).ToList();
            var users = load.Users;
            var names = users.Select(u => u.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
            AssetListDto? existing = null;
            if (row.AssetId is { } id)
                existing = await api.AssetAsync(id, history: false);

            var vm = new AssetEditViewModel(api, cats, locs, users, names, existing, seed: existing is null ? row : null);
            var win = new AssetEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            if (win.ShowDialog() == true)
                await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }
}
