using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Core.Services;

public sealed class CurrentUser
{
    public required User User { get; init; }
    public string Username => User.Username;
    public string DisplayName => string.IsNullOrWhiteSpace(User.Name) ? User.Username : User.Name;
    public string Token { get; init; } = "";
    public UserRole Role => User.Role;
    public bool Can(string permission) => Permissions.Allows(Role, permission);
}
