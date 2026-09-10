using PAV.Core.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.Services;

public static class AssigneeResolver
{
    /// <summary>
    /// Resolves typed assignee text to a unique PAV user, or links/creates one from AD.
    /// If neither succeeds (including when the AD lookup throws, e.g. no Add rights),
    /// falls back to fallbackId if it's still a known user, otherwise leaves the name as
    /// unlinked text.
    /// </summary>
    public static async Task<(int? Id, string Name)> ResolveAsync(
        ApiClient api, List<UserDto> users, string typed, int? fallbackId = null)
    {
        var tuples = users.Select(u => (u.Id, u.Name, u.Username, u.SamAccount)).ToList();
        var unique = UserNameResolver.ResolveUniqueId(tuples, typed);
        if (unique is not null)
        {
            var u = users.First(x => x.Id == unique);
            return (u.Id, u.Name);
        }
        try
        {
            var person = await api.EnsurePersonFromAdAsync(typed);
            if (person is not null)
            {
                users.Add(person);
                return (person.Id, person.Name);
            }
        }
        catch
        {
            /* leave unlinked text if AD cannot create or the caller lacks rights */
        }
        if (fallbackId is { } id && users.Any(u => u.Id == id))
            return (id, typed);
        return (null, typed);
    }
}
