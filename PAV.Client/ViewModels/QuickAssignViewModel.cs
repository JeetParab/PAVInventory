using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Core.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class QuickAssignViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly List<int> _ids;
    private readonly List<UserDto> _users;
    private readonly List<string> _catalog = [];
    private bool _lockName;

    public string Title => $"Assign {_ids.Count} asset(s)";
    public ObservableCollection<string> AssigneeChoices { get; } = [];

    [ObservableProperty] private string? assignedUserName;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool saving;
    [ObservableProperty] private bool suggestOpen;
    [ObservableProperty] private string hint = "Type a name or user id. Suggestions appear as you type.";

    public string? AppliedName { get; private set; }
    public int? AppliedUserId { get; private set; }
    public int AppliedCount { get; private set; }

    public event Action<bool>? CloseRequested;

    public QuickAssignViewModel(ApiClient api, List<int> ids, List<UserDto> users, List<string> assigneeNames)
    {
        _api = api;
        _ids = ids;
        _users = users;
        foreach (var n in users.Select(u => u.AssignLabel)
                     .Concat(assigneeNames)
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n))
            _catalog.Add(n);
        ApplySuggest("");
        _ = LoadAdAsync();
    }

    private async Task LoadAdAsync()
    {
        try
        {
            var ad = await _api.AdDirectoryAsync();
            var extra = false;
            foreach (var n in ad.Select(a => a.AssignLabel))
            {
                if (_catalog.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
                _catalog.Add(n);
                extra = true;
            }
            if (extra)
            {
                _catalog.Sort(StringComparer.OrdinalIgnoreCase);
                if (!AssigneeSuggest.IsExact(_catalog, AssignedUserName))
                    ApplySuggest(AssignedUserName);
            }
        }
        catch
        {
            /* type from PAV people if AD list cannot load */
        }
    }

    partial void OnAssignedUserNameChanged(string? value)
    {
        if (_lockName) return;
        if (AssigneeSuggest.IsExact(_catalog, value))
        {
            SuggestOpen = false;
            Hint = "Selected. Press Assign.";
            return;
        }
        ApplySuggest(value);
        SuggestOpen = !string.IsNullOrWhiteSpace(value) && AssigneeChoices.Count > 0;
        Hint = string.IsNullOrWhiteSpace(value)
            ? "Type a name or user id. Suggestions appear as you type."
            : AssigneeChoices.Count == 0
                ? "No match in PAV Users or AD users."
                : AssigneeChoices.Count == 1
                    ? "1 match — pick it or press Assign."
                    : $"{AssigneeChoices.Count} matches. Keep typing or pick from the list.";
    }

    private void ApplySuggest(string? value)
    {
        var keep = value;
        _lockName = true;
        AssigneeSuggest.Replace(AssigneeChoices, AssigneeSuggest.Filter(_catalog, value));
        AssignedUserName = keep;
        _lockName = false;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() =>
        {
            if (string.Equals(AssignedUserName, keep, StringComparison.Ordinal)) return;
            _lockName = true;
            AssignedUserName = keep;
            _lockName = false;
        });
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
                var tuples = _users.Select(u => (u.Id, u.Name, u.Username, u.SamAccount)).ToList();
                id = UserNameResolver.ResolveUniqueId(tuples, name);
                if (id is null)
                {
                    var person = await _api.EnsurePersonFromAdAsync(name);
                    if (person is not null)
                    {
                        _users.Add(person);
                        id = person.Id;
                        name = person.Name;
                    }
                }
                else
                {
                    var u = _users.First(x => x.Id == id);
                    name = u.Name;
                }
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
