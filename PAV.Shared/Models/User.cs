using PAV.Shared.Enums;

namespace PAV.Shared.Models;

public class User
{
    public int Id { get; set; }
    public string? EmployeeId { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Department { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public UserRole Role { get; set; } = UserRole.Engineer;
    public bool IsActive { get; set; } = true;
    /// <summary>Set when a known default password is detected at login (legacy installs).</summary>
    public bool MustChangePassword { get; set; }
}
