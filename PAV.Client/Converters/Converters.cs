using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PAV.Shared.Enums;

namespace PAV.Client.Converters;

public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo culture) =>
        value is not true;
    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        value is not true;
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
        "In Use" or "InUse" => Brush("#E7F6EE"),
        "In Stock" or "InStock" => Brush("#E8F0FE"),
        "Low Stock" => Brush("#FFF4E0"),
        "Out of Stock" => Brush("#FDECEC"),
        "Under Repair" or "UnderRepair" => Brush("#FFF4E0"),
        "Standby" => Brush("#F3EDFA"),
        "Damaged" => Brush("#FDECEC"),
        "Lost" => Brush("#FBEFE8"),
        "Retired" or "Disposed" => Brush("#EEF1F4"),
        _ => Brush("#EEF1F4")
    };

    public static Brush Fg(string status) => status switch
    {
        "In Use" or "InUse" => Brush("#107C41"),
        "In Stock" or "InStock" => Brush("#2F6FED"),
        "Low Stock" => Brush("#C47B17"),
        "Out of Stock" => Brush("#D13438"),
        "Under Repair" or "UnderRepair" => Brush("#C47B17"),
        "Standby" => Brush("#6B4C9A"),
        "Damaged" => Brush("#D13438"),
        "Lost" => Brush("#8A3B12"),
        "Retired" or "Disposed" => Brush("#5C6B7A"),
        _ => Brush("#5C6B7A")
    };

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

public class StringNotEmptyToVis : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class StatusOptions
{
    public static IReadOnlyList<string> DisplayNames { get; } =
        AssetStatusNames.All.Select(s => s.Display()).ToList();
}
