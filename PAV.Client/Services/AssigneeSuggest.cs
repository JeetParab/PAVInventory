namespace PAV.Client.Services;

public static class AssigneeSuggest
{
    public const int MaxShown = 25;

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
