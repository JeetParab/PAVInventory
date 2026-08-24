namespace PAV.Shared.Models;

public class IpRange
{
    public int Id { get; set; }
    public int FloorNumber { get; set; }
    public string Name { get; set; } = "";
    public int ThirdOctet { get; set; }
    public string Cidr { get; set; } = "";
    public string? GatewayIp { get; set; }
    public string? Notes { get; set; }

    public List<IpRecord> Addresses { get; set; } = [];
}
