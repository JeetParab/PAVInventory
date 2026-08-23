using PAV.Shared.Models;

namespace PAV.Core.Services;

/// <summary>
/// A name maps to a User Id only when exactly one user matches on Name or Username
/// (trim + case-insensitive). Zero or multiple matches return null.
/// </summary>
public static class UserNameResolver
{
    public static int? ResolveUniqueId(
        IEnumerable<(int Id, string Name, string Username)> users,
        string? raw)
    {
        var key = Mapping.Clean(raw);
        if (key is null)
            return null;

        var matches = users
            .Where(u =>
                string.Equals(u.Name?.Trim(), key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Username?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Id)
            .Distinct()
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
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
}
