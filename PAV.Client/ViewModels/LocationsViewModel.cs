using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class LocationsViewModel(ApiClient api, ShellViewModel shell) : ObservableObject
{
    public ObservableCollection<LocationDto> Locations { get; } = [];
    [ObservableProperty] private LocationDto? selected;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string? description;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string? error;

    public bool CanManage => shell.CanManageLocations;

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            var list = await api.LocationsAsync();
            Locations.Clear();
            foreach (var l in list) Locations.Add(l);
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

    partial void OnSelectedChanged(LocationDto? value)
    {
        Name = value?.Name ?? "";
        Description = value?.Description;
        Error = null;
    }

    [RelayCommand]
    private void NewItem()
    {
        Selected = null;
        Name = "";
        Description = null;
        Error = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanManage) return;
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Name is required.";
            return;
        }
        try
        {
            var req = new SaveLocationRequest { Name = Name.Trim(), Description = Description };
            if (Selected is null) await api.CreateLocationAsync(req);
            else await api.UpdateLocationAsync(Selected.Id, req);
            await LoadAsync();
            NewItem();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null || !CanManage) return;
        if (!Ui.Confirm($"Delete location '{Selected.Name}'?"))
            return;
        try
        {
            await api.DeleteLocationAsync(Selected.Id);
            await LoadAsync();
            NewItem();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
