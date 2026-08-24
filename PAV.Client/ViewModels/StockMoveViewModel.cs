using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class StockMoveViewModel : ObservableObject
{
    private readonly ApiClient _api;
    public string Kind { get; }

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string actionLabel = "Save";
    [ObservableProperty] private StockItemDto? item;
    [ObservableProperty] private int quantity = 1;
    [ObservableProperty] private string? assignedUserName;
    [ObservableProperty] private string? reference;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? serialNumber;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool needsUser;
    [ObservableProperty] private bool needsReason;
    [ObservableProperty] private string hint = "";

    public ObservableCollection<StockItemDto> Items { get; } = [];
    public ObservableCollection<UserDto> Users { get; } = [];
    public event Action<bool>? CloseRequested;

    public StockMoveViewModel(ApiClient api, string kind, List<StockItemDto> items, StockItemDto? selected)
    {
        _api = api;
        Kind = kind;
        foreach (var i in items.Where(x => x.IsActive))
            Items.Add(i);
        Item = selected is { IsActive: true } ? Items.FirstOrDefault(x => x.Id == selected.Id) : Items.FirstOrDefault();
        Title = kind switch
        {
            "Receive" => "Receive stock",
            "Issue" => "Issue stock",
            "Return" => "Return stock",
            "Adjust" => "Adjust stock",
            _ => "Stock"
        };
        ActionLabel = kind;
        NeedsUser = kind is "Issue" or "Return";
        NeedsReason = kind == "Adjust";
        _ = LoadUsersAsync();
        UpdateHint();
    }

    partial void OnItemChanged(StockItemDto? value) => UpdateHint();
    partial void OnQuantityChanged(int value) => UpdateHint();

    private void UpdateHint()
    {
        if (Item is null)
        {
            Hint = "";
            return;
        }
        var current = Item.OnHand;
        Hint = Kind switch
        {
            "Receive" => $"On hand {current}  →  {current + Math.Max(Quantity, 0)}",
            "Issue" => $"On hand {current}  →  {current - Math.Max(Quantity, 0)}",
            "Return" => $"On hand {current}  →  {current + Math.Max(Quantity, 0)}",
            "Adjust" => $"On hand {current}  →  {current + Quantity}",
            _ => $"On hand {current}"
        };
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var list = await _api.UsersAsync();
            Users.Clear();
            foreach (var u in list.Where(x => x.IsActive).OrderBy(x => x.Name))
                Users.Add(u);
        }
        catch
        {
            // type the name if the user list cannot load
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = null;
        if (Item is null)
        {
            Error = "Pick an item.";
            return;
        }
        try
        {
            var req = new StockMoveRequest
            {
                StockItemId = Item.Id,
                Quantity = Quantity,
                AssignedUserName = AssignedUserName,
                Reference = Reference,
                Notes = Notes,
                SerialNumber = SerialNumber
            };
            var match = Users.FirstOrDefault(u =>
                string.Equals(u.Name, AssignedUserName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Username, AssignedUserName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                req.UserId = match.Id;

            if (Kind == "Receive") await _api.StockReceiveAsync(req);
            else if (Kind == "Issue") await _api.StockIssueAsync(req);
            else if (Kind == "Return") await _api.StockReturnAsync(req);
            else if (Kind == "Adjust") await _api.StockAdjustAsync(req);
            else throw new InvalidOperationException(Kind);

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
