using System.Collections;

namespace PAV.Client.Services;

public static class Theme
{
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app is null) return;
        IsDark = dark;

        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var colors = new ResourceDictionary { Source = uri };
        var dicts = app.Resources.MergedDictionaries;

        var replaced = false;
        for (var i = 0; i < dicts.Count; i++)
        {
            var src = dicts[i].Source?.OriginalString ?? "";
            if (src.Contains("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                dicts[i] = colors;
                replaced = true;
                break;
            }
        }
        if (!replaced)
            dicts.Insert(0, colors);

        foreach (DictionaryEntry entry in colors)
        {
            if (entry.Value is SolidColorBrush brush)
                app.Resources[entry.Key] = new SolidColorBrush(brush.Color) { Opacity = brush.Opacity };
            else
                app.Resources[entry.Key] = entry.Value;
        }
        Changed?.Invoke();
    }
}
