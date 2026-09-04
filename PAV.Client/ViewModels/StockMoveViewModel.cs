using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Core.Services;
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
    [ObservableProperty] private string userHint = "";
    private bool _lockUser;

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
    partial void OnAssignedUserNameChanged(string? value)
    {
        if (!_lockUser && !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 2)
        {
            var key = value.Trim();
            var hits = Users
                .Select(u => u.AssignLabel)
                .Where(n => n.Contains(key, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (hits.Count == 1 && !hits[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                _lockUser = true;
                AssignedUserName = hits[0];
                _lockUser = false;
            }
        }
        UpdateUserHint();
    }

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

    private void UpdateUserHint()
    {
        if (!NeedsUser)
        {
            UserHint = "";
            return;
        }
        var tuples = Users.Select(u => (u.Id, u.Name, u.Username, u.SamAccount)).ToList();
        UserHint = UserNameResolver.Describe(tuples, AssignedUserName);
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var list = await _api.UsersAsync();
            Users.Clear();
            foreach (var u in list.Where(x => x.IsActive).OrderBy(x => x.Name))
                Users.Add(u);
            UpdateUserHint();
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
            var tuples = Users.Select(u => (u.Id, u.Name, u.Username, u.SamAccount)).ToList();
            if (NeedsUser && UserNameResolver.MatchCount(tuples, AssignedUserName) > 1)
            {
                // keep free text — do not send a UserId
            }

            var req = new StockMoveRequest
            {
                StockItemId = Item.Id,
                Quantity = Quantity,
                UserId = null,
                AssignedUserName = AssignedUserName,
                Reference = Reference,
                Notes = Notes,
                SerialNumber = SerialNumber
            };

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
