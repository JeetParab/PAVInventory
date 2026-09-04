namespace PAV.Shared.Models;

/// <summary>
/// One-time snapshot of an AD user id (SAM) plus the fields we need to fill
/// a PAV person when ManageEngine last logon matches.
/// </summary>
public class AdDirectoryEntry
{
    public int Id { get; set; }
    public string Sam { get; set; } = "";
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}
