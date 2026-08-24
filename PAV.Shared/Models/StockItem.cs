namespace PAV.Shared.Models;

public class StockItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedKey { get; set; } = "";
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string Unit { get; set; } = "pcs";
    public int OnHand { get; set; }
    public int MinimumQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NeedsReview { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<StockMovement> Movements { get; set; } = [];
}
