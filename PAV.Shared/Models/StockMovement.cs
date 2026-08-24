using PAV.Shared.Enums;

namespace PAV.Shared.Models;

public class StockMovement
{
    public int Id { get; set; }
    public int StockItemId { get; set; }
    public StockItem StockItem { get; set; } = null!;
    public StockMovementType MovementType { get; set; }
    /// <summary>Positive for Opening/Receive/Issue/Return. Signed for Adjustment.</summary>
    public int Quantity { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string? AssignedUserName { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public string? SerialNumber { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
