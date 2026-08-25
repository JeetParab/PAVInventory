using PAV.Shared.Models;

namespace PAV.Core.Services;

/// <summary>
/// A name maps to a User Id only when exactly one user matches on Name or Username
/// (trim + case-insensitive). Zero or multiple matches return null.
/// Never pick the first of several matches.
/// </summary>
public static class UserNameResolver
{
    public static List<int> MatchIds(
        IEnumerable<(int Id, string Name, string Username)> users,
        string? raw)
    {
        var key = Mapping.Clean(raw);
        if (key is null)
            return [];

        return users
            .Where(u =>
                string.Equals(u.Name?.Trim(), key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Username?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Id)
            .Distinct()
            .ToList();
    }

    public static int MatchCount(
        IEnumerable<(int Id, string Name, string Username)> users,
        string? raw) => MatchIds(users, raw).Count;

    public static int? ResolveUniqueId(
        IEnumerable<(int Id, string Name, string Username)> users,
        string? raw)
    {
        var matches = MatchIds(users, raw);
        return matches.Count == 1 ? matches[0] : null;
    }

    public static string Describe(IEnumerable<(int Id, string Name, string Username)> users, string? raw)
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
        IReadOnlyList<(int Id, string Name, string Username)> users)
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

        asset.AssignedUserName = name;
        asset.AssignedUserId = ResolveUniqueId(users, name);
    }

    /// <summary>
    /// Same rule as asset assignment: explicit UserId must exist; otherwise unique
    /// name match only. Ambiguous / unknown names stay as free text with UserId null.
    /// </summary>
    public static (int? UserId, string? UserName) ResolveMovement(
        int? userId,
        string? userName,
        IReadOnlyList<(int Id, string Name, string Username)> users)
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
        return (ResolveUniqueId(users, name), name);
    }
}
