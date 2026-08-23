using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class QuickAssignViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly List<int> _ids;
    private readonly List<UserDto> _users;

    public string Title => $"Assign {_ids.Count} asset(s)";
    public List<string> AssigneeChoices { get; }

    [ObservableProperty] private string? assignedUserName;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool saving;

    public string? AppliedName { get; private set; }
    public int? AppliedUserId { get; private set; }
    public int AppliedCount { get; private set; }

    public event Action<bool>? CloseRequested;

    public QuickAssignViewModel(ApiClient api, List<int> ids, List<UserDto> users, List<string> assigneeNames)
    {
        _api = api;
        _ids = ids;
        _users = users;
        AssigneeChoices = assigneeNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
    }

    [RelayCommand]
    private async Task AssignAsync()
    {
        if (string.IsNullOrWhiteSpace(AssignedUserName))
        {
            Error = "Pick or type a user name, or click Unassign.";
            return;
        }
        await PatchAsync(AssignedUserName.Trim());
    }

    [RelayCommand]
    private Task UnassignAsync() => PatchAsync(null);

    private async Task PatchAsync(string? name)
    {
        Error = null;
        Saving = true;
        try
        {
            int? id = null;
            if (name is not null)
            {
                var matches = _users
                    .Where(u =>
                        string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase))
                    .Select(u => u.Id)
                    .Distinct()
                    .ToList();
                if (matches.Count == 1)
                    id = matches[0];
            }

            var n = await _api.BulkPatchAsync(new BulkEditRequest
            {
                Ids = _ids,
                SetAssignedUser = true,
                AssignedUserId = id,
                AssignedUserName = name
            });
            AppliedName = name;
            AppliedUserId = id;
            AppliedCount = n;
            Ui.Info(name is null ? $"Unassigned {n} assets." : $"Assigned {n} assets to {name}.");
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
