namespace PAV.Shared.Enums;

public enum StockMovementType
{
    OpeningBalance = 0,
    Receive = 1,
    Issue = 2,
    Return = 3,
    Adjustment = 4
}

public static class StockMovementTypeNames
{
    public static string Display(this StockMovementType type) => type switch
    {
        StockMovementType.OpeningBalance => "Opening",
        StockMovementType.Receive => "Receive",
        StockMovementType.Issue => "Issue",
        StockMovementType.Return => "Return",
        StockMovementType.Adjustment => "Adjust",
        _ => type.ToString()
    };

    /// <summary>Signed quantity applied to on-hand. Issue is negative; Adjustment is already signed.</summary>
    public static int SignedDelta(this StockMovementType type, int quantity) => type switch
    {
        StockMovementType.Issue => -Math.Abs(quantity),
        StockMovementType.Adjustment => quantity,
        _ => Math.Abs(quantity)
    };
}
