using System.Globalization;
using PAV.Client.Services;

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
        var pill = For(value?.ToString() ?? "");
        return parameter?.ToString() == "fg" ? pill.Fg : pill.Bg;
    }

    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Pill For(string status) => status switch
    {
        "In Use" or "InUse" => Green,
        "In Stock" or "InStock" => Blue,
        "Low Stock" or "Under Repair" or "UnderRepair" => Amber,
        "Out of Stock" or "Damaged" => Red,
        "Standby" => Purple,
        "Lost" => Rust,
        _ => Grey
    };

    // One shared brush pair per colour, recoloured in place on theme change so every
    // pill already on screen updates without re-rendering the grid.
    private static readonly Pill Green = new(0xE7F6EE, 0x107C41, 0x1C3829, 0x6FD39A);
    private static readonly Pill Blue = new(0xE8F0FE, 0x2F6FED, 0x1E2F4D, 0x8DB6FF);
    private static readonly Pill Amber = new(0xFFF4E0, 0xC47B17, 0x3A2E17, 0xF2B350);
    private static readonly Pill Red = new(0xFDECEC, 0xD13438, 0x3F2124, 0xFF8A8D);
    private static readonly Pill Purple = new(0xF3EDFA, 0x6B4C9A, 0x2E2542, 0xC4A8F2);
    private static readonly Pill Rust = new(0xFBEFE8, 0x8A3B12, 0x3B261A, 0xEFA27A);
    private static readonly Pill Grey = new(0xEEF1F4, 0x5C6B7A, 0x2A3441, 0xAEBBCB);
    private static readonly Pill[] All = [Green, Blue, Amber, Red, Purple, Rust, Grey];

    static StatusBrushConverter()
    {
        Recolor();
        Theme.Changed += Recolor;
    }

    private static void Recolor()
    {
        foreach (var p in All)
            p.Apply(Theme.IsDark);
    }

    private sealed class Pill(uint lightBg, uint lightFg, uint darkBg, uint darkFg)
    {
        public SolidColorBrush Bg { get; } = new();
        public SolidColorBrush Fg { get; } = new();

        public void Apply(bool dark)
        {
            Bg.Color = Rgb(dark ? darkBg : lightBg);
            Fg.Color = Rgb(dark ? darkFg : lightFg);
        }

        private static Color Rgb(uint v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}

public class StringNotEmptyToVis : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
