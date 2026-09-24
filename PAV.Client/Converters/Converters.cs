using System.Globalization;

namespace PAV.Client.Converters;

public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo culture) =>
        value is not true;
    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        value is not true;
}

public class PermissionAndSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is true && values[1] is int count && count > 0;

    public object[] ConvertBack(object value, Type[] t, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public class BoolToVis : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class NullToVis : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo culture)
    {
        var key = value?.ToString() ?? "";
        return parameter?.ToString() == "fg" ? Fg(key) : Bg(key);
    }

    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static Brush Bg(string status) => status switch
    {
        "In Use" or "InUse" => BgGreen,
        "In Stock" or "InStock" => BgBlue,
        "Low Stock" => BgAmber,
        "Out of Stock" => BgRed,
        "Under Repair" or "UnderRepair" => BgAmber,
        "Standby" => BgPurple,
        "Damaged" => BgRed,
        "Lost" => BgRust,
        _ => BgGrey
    };

    public static Brush Fg(string status) => status switch
    {
        "In Use" or "InUse" => FgGreen,
        "In Stock" or "InStock" => FgBlue,
        "Low Stock" => FgAmber,
        "Out of Stock" => FgRed,
        "Under Repair" or "UnderRepair" => FgAmber,
        "Standby" => FgPurple,
        "Damaged" => FgRed,
        "Lost" => FgRust,
        _ => FgGrey
    };

    // Called three times per grid row; share frozen brushes instead of parsing hex each time.
    private static readonly Brush BgGreen = Brush(0xE7, 0xF6, 0xEE), FgGreen = Brush(0x10, 0x7C, 0x41);
    private static readonly Brush BgBlue = Brush(0xE8, 0xF0, 0xFE), FgBlue = Brush(0x2F, 0x6F, 0xED);
    private static readonly Brush BgAmber = Brush(0xFF, 0xF4, 0xE0), FgAmber = Brush(0xC4, 0x7B, 0x17);
    private static readonly Brush BgRed = Brush(0xFD, 0xEC, 0xEC), FgRed = Brush(0xD1, 0x34, 0x38);
    private static readonly Brush BgPurple = Brush(0xF3, 0xED, 0xFA), FgPurple = Brush(0x6B, 0x4C, 0x9A);
    private static readonly Brush BgRust = Brush(0xFB, 0xEF, 0xE8), FgRust = Brush(0x8A, 0x3B, 0x12);
    private static readonly Brush BgGrey = Brush(0xEE, 0xF1, 0xF4), FgGrey = Brush(0x5C, 0x6B, 0x7A);

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public class StringNotEmptyToVis : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
