using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class DashboardViewModel(ApiClient api, ShellViewModel shell) : ObservableObject
{
    [ObservableProperty] private DashboardDto? data;
    [ObservableProperty] private bool loading;

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            Data = await api.DashboardAsync();
        }
        finally
        {
            Loading = false;
        }
    }

    [RelayCommand]
    private Task OpenTotal() => shell.ShowInventoryAsync();

    [RelayCommand]
    private Task OpenInUse() => shell.ShowInventoryAsync(AssetStatus.InUse.Display());

    [RelayCommand]
    private Task OpenInStock() => shell.ShowInventoryAsync(AssetStatus.InStock.Display());

    [RelayCommand]
    private Task OpenRepair() => shell.ShowInventoryAsync(AssetStatus.UnderRepair.Display());

    [RelayCommand]
    private Task OpenDamaged() => shell.ShowInventoryAsync(AssetStatus.Damaged.Display());

    [RelayCommand]
    private Task OpenStandby() => shell.ShowInventoryAsync(AssetStatus.Standby.Display());

    [RelayCommand]
    private Task OpenCategory(CategoryCountDto? cat) =>
        cat is null ? Task.CompletedTask : shell.ShowInventoryAsync(categoryId: cat.CategoryId);
}
