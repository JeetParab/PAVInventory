using PAV.Shared.Enums;

namespace PAV.Shared.Models;

public class AssetHistory
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public string Username { get; set; } = "";
    public HistoryAction Action { get; set; }
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; }
}
