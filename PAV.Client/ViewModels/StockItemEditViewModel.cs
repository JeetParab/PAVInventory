using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Core.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class StockItemEditViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly int? _id;

    [ObservableProperty] private string title = "Add stock item";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string? manufacturer;
    [ObservableProperty] private string? model;
    [ObservableProperty] private string? category = "Peripheral";
    [ObservableProperty] private string unit = "pcs";
    [ObservableProperty] private int minimumQuantity;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? error;

    public ObservableCollection<string> Categories { get; } = new(StockExcel.Categories);
    public event Action<bool>? CloseRequested;

    public StockItemEditViewModel(ApiClient api, StockItemDto? existing, string? defaultCategory = null)
    {
        _api = api;
        if (existing is null)
        {
            if (!string.IsNullOrWhiteSpace(defaultCategory))
            {
                Category = defaultCategory;
                if (defaultCategory.Equals("Toner", StringComparison.OrdinalIgnoreCase))
                    Title = "Add toner";
            }
            return;
        }
        _id = existing.Id;
        Title = "Edit stock item";
        Name = existing.Name;
        Manufacturer = existing.Manufacturer;
        Model = existing.Model;
        Category = existing.Category ?? "Other";
        Unit = existing.Unit;
        MinimumQuantity = existing.MinimumQuantity;
        IsActive = existing.IsActive;
        Notes = existing.Notes;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        try
        {
            await _api.SaveStockItemAsync(_id, new SaveStockItemRequest
            {
                Name = Name,
                Manufacturer = Manufacturer,
                Model = Model,
                Category = Category,
                Unit = Unit,
                MinimumQuantity = MinimumQuantity,
                IsActive = IsActive,
                Notes = Notes
            });
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
