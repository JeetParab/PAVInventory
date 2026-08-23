using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class BulkAddViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public List<CategoryDto> Categories { get; }
    public List<LocationDto> Locations { get; }
    public List<string> Statuses { get; } = AssetStatusNames.All.Select(s => s.Display()).ToList();

    [ObservableProperty] private int categoryId;
    [ObservableProperty] private int locationId;
    [ObservableProperty] private string status = AssetStatus.InStock.Display();
    [ObservableProperty] private string? manufacturer;
    [ObservableProperty] private string? model;
    [ObservableProperty] private string? designation;
    [ObservableProperty] private string? domain;
    [ObservableProperty] private string lines = "";
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool saving;

    public event Action<bool>? CloseRequested;

    public BulkAddViewModel(ApiClient api, List<CategoryDto> categories, List<LocationDto> locations)
    {
        _api = api;
        Categories = categories;
        Locations = [new LocationDto { Id = 0, Name = "(None)" }, .. locations];
        CategoryId = categories.FirstOrDefault()?.Id ?? 0;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        Error = null;
        if (CategoryId == 0)
        {
            Error = "Category is required.";
            return;
        }
        if (!AssetStatusNames.TryParse(Status, out var st))
        {
            Error = "Status is required.";
            return;
        }
        Saving = true;
        try
        {
            var n = await _api.BulkAddAsync(new BulkAddRequest
            {
                CategoryId = CategoryId,
                LocationId = LocationId == 0 ? null : LocationId,
                Status = st,
                Manufacturer = Manufacturer,
                Model = Model,
                Designation = Designation,
                Domain = Domain,
                Lines = Lines
            });
            Ui.Info($"Added {n} assets.");
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Saving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
