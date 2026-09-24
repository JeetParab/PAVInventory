using PAV.Shared.Dtos;

namespace PAV.Client.Services;

public static class AssigneeSuggest
{
    public const int MaxShown = 25;

    public static IEnumerable<string> BuildCatalog(IEnumerable<UserDto> users, IEnumerable<string> extraNames) =>
        users.Select(u => u.AssignLabel)
            .Concat(extraNames)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n);

    /// <summary>Adds AD user labels missing from the catalog. Returns true when anything was added.</summary>
    public static async Task<bool> MergeAdAsync(ApiClient api, List<string> catalog)
    {
        var added = false;
        foreach (var n in (await api.AdDirectoryAsync()).Select(a => a.AssignLabel))
        {
            if (catalog.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
            catalog.Add(n);
            added = true;
        }
        if (added)
            catalog.Sort(StringComparer.OrdinalIgnoreCase);
        return added;
    }

    public static bool IsExact(IReadOnlyList<string> all, string? typed)
    {
        var key = typed?.Trim() ?? "";
        return key.Length > 0 && all.Any(n => n.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public static List<string> Filter(IReadOnlyList<string> all, string? typed)
    {
        var key = typed?.Trim() ?? "";
        IEnumerable<string> q = all.Where(n => n.Length > 0);
        if (key.Length == 0)
            return q.Take(MaxShown).ToList();
        return q
            .Where(n => n.Contains(key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.StartsWith(key, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(MaxShown)
            .ToList();
    }

    public static void Replace(System.Collections.ObjectModel.ObservableCollection<string> target, IEnumerable<string> items)
    {
        target.Clear();
        foreach (var n in items)
            target.Add(n);
    }
}
