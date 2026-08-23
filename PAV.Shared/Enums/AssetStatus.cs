namespace PAV.Shared.Enums;

public enum AssetStatus
{
    InUse = 0,
    InStock = 1,
    UnderRepair = 2,
    Standby = 3,
    Damaged = 4,
    Lost = 5,
    Retired = 6,
    Disposed = 7
}

public static class AssetStatusNames
{
    public static readonly IReadOnlyList<AssetStatus> All =
    [
        AssetStatus.InUse,
        AssetStatus.InStock,
        AssetStatus.UnderRepair,
        AssetStatus.Standby,
        AssetStatus.Damaged,
        AssetStatus.Lost,
        AssetStatus.Retired,
        AssetStatus.Disposed
    ];

    public static string Display(this AssetStatus status) => status switch
    {
        AssetStatus.InUse => "In Use",
        AssetStatus.InStock => "In Stock",
        AssetStatus.UnderRepair => "Under Repair",
        AssetStatus.Standby => "Standby",
        AssetStatus.Damaged => "Damaged",
        AssetStatus.Lost => "Lost",
        AssetStatus.Retired => "Retired",
        AssetStatus.Disposed => "Disposed",
        _ => status.ToString()
    };

    public static bool TryParse(string? value, out AssetStatus status)
    {
        status = AssetStatus.InStock;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var n = value.Trim().Replace(" ", "").Replace("_", "").Replace("-", "");
        foreach (var s in All)
        {
            var name = s.ToString();
            var display = s.Display().Replace(" ", "");
            if (n.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                n.Equals(display, StringComparison.OrdinalIgnoreCase))
            {
                status = s;
                return true;
            }
        }

        return false;
    }
}
