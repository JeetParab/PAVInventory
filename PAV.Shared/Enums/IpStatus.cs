namespace PAV.Shared.Enums;

public enum IpStatus
{
    Free = 0,
    Used = 1,
    Reserved = 2,
    Quarantine = 3,
    Deprecated = 4
}

public static class IpStatusNames
{
    public static readonly IReadOnlyList<IpStatus> All =
    [
        IpStatus.Free,
        IpStatus.Used,
        IpStatus.Reserved,
        IpStatus.Quarantine,
        IpStatus.Deprecated
    ];

    public static string Display(this IpStatus status) => status switch
    {
        IpStatus.Free => "Free",
        IpStatus.Used => "Used",
        IpStatus.Reserved => "Reserved",
        IpStatus.Quarantine => "Quarantine",
        IpStatus.Deprecated => "Deprecated",
        _ => status.ToString()
    };

    public static bool TryParse(string? value, out IpStatus status)
    {
        status = IpStatus.Free;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var n = value.Trim().Replace(" ", "");
        foreach (var s in All)
        {
            if (n.Equals(s.ToString(), StringComparison.OrdinalIgnoreCase) ||
                n.Equals(s.Display(), StringComparison.OrdinalIgnoreCase))
            {
                status = s;
                return true;
            }
        }

        return false;
    }
}
