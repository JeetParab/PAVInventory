using PAV.Shared.Models;

namespace PAV.Core.Services;

/// <summary>
/// A name maps to a User Id only when exactly one user matches on Name, Username
/// or AD user id (trim + case-insensitive). Zero or multiple matches return null.
/// Never pick the first of several matches.
/// </summary>
public static class UserNameResolver
{
    public static List<int> MatchIds(
        IEnumerable<(int Id, string Name, string Username)> users,
        string? raw) =>
        MatchIds(users.Select(u => (u.Id, u.Name, u.Username, (string?)null)), raw);

    public static List<int> MatchIds(
        IEnumerable<(int Id, string Name, string Username, string? Sam)> users,
        string? raw)
    {
        Split(raw, out var key, out var namePart, out var idPart);
        if (key is null)
            return [];

        return users
            .Where(u => Hits(u.Name, u.Username, u.Sam, key, namePart, idPart))
            .Select(u => u.Id)
            .Distinct()
            .ToList();
    }

    public static int MatchCount(
        IEnumerable<(int Id, string Name, string Username)> users,
        string? raw) => MatchIds(users, raw).Count;

    public static int MatchCount(
        IEnumerable<(int Id, string Name, string Username, string? Sam)> users,
        string? raw) => MatchIds(users, raw).Count;

    public static int? ResolveUniqueId(
        IEnumerable<(int Id, string Name, string Username)> users,
        string? raw)
    {
        var matches = MatchIds(users, raw);
        return matches.Count == 1 ? matches[0] : null;
    }

    public static int? ResolveUniqueId(
        IEnumerable<(int Id, string Name, string Username, string? Sam)> users,
        string? raw)
    {
        var matches = MatchIds(users, raw);
        return matches.Count == 1 ? matches[0] : null;
    }

    public static string Describe(IEnumerable<(int Id, string Name, string Username)> users, string? raw) =>
        Describe(users.Select(u => (u.Id, u.Name, u.Username, (string?)null)), raw);

    public static string Describe(
        IEnumerable<(int Id, string Name, string Username, string? Sam)> users,
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var n = MatchCount(users, raw);
        if (n == 1) return "Unique PAV user — will be linked.";
        if (n > 1) return "Multiple PAV users match this name — stored as text, not linked.";
        return "No PAV user match — stored as text.";
    }

    /// <summary>
    /// AssignedUserId wins when it points at a real user. Otherwise the free-text
    /// name is stored and linked only when the match is unique.
    /// </summary>
    public static void ApplyTo(
        Asset asset,
        int? userId,
        string? userName,
        IReadOnlyList<(int Id, string Name, string Username)> users) =>
        ApplyTo(asset, userId, userName, users.Select(u => (u.Id, u.Name, u.Username, (string?)null)).ToList());

    public static void ApplyTo(
        Asset asset,
        int? userId,
        string? userName,
        IReadOnlyList<(int Id, string Name, string Username, string? Sam)> users)
    {
        if (userId is { } id)
        {
            foreach (var u in users)
            {
                if (u.Id != id) continue;
                asset.AssignedUserId = u.Id;
                asset.AssignedUserName = Mapping.Clean(u.Name) ?? Mapping.Clean(u.Username);
                return;
            }
        }

        var name = Mapping.Clean(userName);
        if (name is null)
        {
            asset.AssignedUserId = null;
            asset.AssignedUserName = null;
            return;
        }

        var match = users.Where(u =>
        {
            Split(name, out var key, out var namePart, out var idPart);
            return key is not null && Hits(u.Name, u.Username, u.Sam, key, namePart, idPart);
        }).Take(2).ToList();

        if (match.Count == 1)
        {
            asset.AssignedUserId = match[0].Id;
            asset.AssignedUserName = Mapping.Clean(match[0].Name) ?? Mapping.Clean(match[0].Username);
            return;
        }

        Split(name, out _, out var display, out _);
        asset.AssignedUserName = display ?? name;
        asset.AssignedUserId = null;
    }

    public static (int? UserId, string? UserName) ResolveMovement(
        int? userId,
        string? userName,
        IReadOnlyList<(int Id, string Name, string Username)> users) =>
        ResolveMovement(userId, userName, users.Select(u => (u.Id, u.Name, u.Username, (string?)null)).ToList());

    public static (int? UserId, string? UserName) ResolveMovement(
        int? userId,
        string? userName,
        IReadOnlyList<(int Id, string Name, string Username, string? Sam)> users)
    {
        if (userId is { } id)
        {
            var hit = users.Where(u => u.Id == id).Take(1).ToList();
            if (hit.Count == 0)
                throw new AppException(400, "validation", "User not found.");
            var u = hit[0];
            return (u.Id, Mapping.Clean(u.Name) ?? Mapping.Clean(u.Username));
        }

        var name = Mapping.Clean(userName);
        if (name is null)
            return (null, null);
        var uid = ResolveUniqueId(users, name);
        if (uid is { } found)
        {
            var u = users.First(x => x.Id == found);
            return (u.Id, Mapping.Clean(u.Name) ?? Mapping.Clean(u.Username));
        }
        Split(name, out _, out var display, out _);
        return (null, display ?? name);
    }

    public static string Display(string? name, string? sam)
    {
        var n = Mapping.Clean(name) ?? "";
        var id = Mapping.Clean(sam);
        if (id is null || id.StartsWith("person:", StringComparison.OrdinalIgnoreCase))
            return n;
        if (n.Length == 0) return id;
        return n + "  (" + id + ")";
    }

    private static bool Hits(string? name, string? username, string? sam, string key, string? namePart, string? idPart)
    {
        return Eq(name, key) || Eq(username, key) || Eq(sam, key)
               || (namePart is not null && Eq(name, namePart))
               || (idPart is not null && (Eq(username, idPart) || Eq(sam, idPart)));
    }

    private static bool Eq(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value.Trim(), key, StringComparison.OrdinalIgnoreCase);

    private static void Split(string? raw, out string? key, out string? namePart, out string? idPart)
    {
        key = Mapping.Clean(raw);
        namePart = null;
        idPart = null;
        if (key is null) return;
        if (key.EndsWith(')') )
        {
            var open = key.LastIndexOf('(');
            if (open > 0)
            {
                idPart = Mapping.Clean(key[(open + 1)..^1]);
                namePart = Mapping.Clean(key[..open]);
            }
        }
    }
}
