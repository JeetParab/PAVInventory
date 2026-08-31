using PAV.Shared.Enums;

namespace PAV.Shared.Models;

public class IpRecord
{
    public int Id { get; set; }
    public int RangeId { get; set; }
    public IpRange Range { get; set; } = null!;
    public string Address { get; set; } = "";
    public int HostOctet { get; set; }
    public IpStatus Status { get; set; } = IpStatus.Free;
    public string? AssignedDevice { get; set; }
    public string? AssignedUser { get; set; }
    public string? Department { get; set; }
    public DateTime? DateAssigned { get; set; }
    public string? MacAddress { get; set; }
    public string? DeviceType { get; set; }
    public string? Notes { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string? AllocatedBy { get; set; }
    public bool IsTemporary { get; set; }
}
