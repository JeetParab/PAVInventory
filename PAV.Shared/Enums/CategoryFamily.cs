namespace PAV.Shared.Enums;

public enum CategoryFamily
{
    Computer = 0,
    Peripheral = 1
}

public static class CategoryFamilies
{
    public static CategoryFamily FromName(string? name)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "laptop":
            case "desktop":
            case "all-in-one":
            case "all in one":
            case "aio":
                return CategoryFamily.Computer;
            case "network":
            case "monitor":
            case "printer":
            case "ups":
            case "accessory":
            case "other":
                return CategoryFamily.Peripheral;
            default:
                return CategoryFamily.Computer;
        }
    }

    public static string Display(this CategoryFamily family) =>
        family == CategoryFamily.Peripheral ? "Peripherals" : "Computers";
}
