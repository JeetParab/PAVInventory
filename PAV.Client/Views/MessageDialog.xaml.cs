using System.Windows;
using System.Windows.Media;

namespace PAV.Client.Views;

public partial class MessageDialog : Window
{
    public bool Confirmed { get; private set; }

    public enum Kind
    {
        Info,
        Warning,
        Question
    }

    public MessageDialog(string title, string message, Kind kind, bool yesNo)
    {
        InitializeComponent();
        Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow;

        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "PAV IT Inventory for SIDBI" : title;
        MessageText.Text = message;

        switch (kind)
        {
            case Kind.Question:
                IconGlyph.Text = "\uE9CE"; // Help
                IconBadge.Background = BrushFrom("#3B82F6", 0.18);
                IconGlyph.Foreground = BrushFrom("#3B82F6");
                break;
            case Kind.Warning:
                IconGlyph.Text = "\uE7BA"; // Warning
                IconBadge.Background = BrushFrom("#F59E0B", 0.18);
                IconGlyph.Foreground = BrushFrom("#D97706");
                break;
            default:
                IconGlyph.Text = "\uE946"; // Info
                IconBadge.Background = BrushFrom("#3B82F6", 0.18);
                IconGlyph.Foreground = BrushFrom("#3B82F6");
                break;
        }

        if (yesNo)
        {
            PrimaryButton.Content = "Yes";
            SecondaryButton.Content = "No";
            SecondaryButton.Visibility = Visibility.Visible;
        }
        else
        {
            PrimaryButton.Content = "OK";
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
    }

    private static SolidColorBrush BrushFrom(string hex, double opacity = 1)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        c.A = (byte)Math.Clamp((int)(opacity * 255), 0, 255);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    public static bool Confirm(string message, string? title = null)
    {
        var dlg = new MessageDialog(
            title ?? "Confirm",
            message,
            Kind.Question,
            yesNo: true);
        return dlg.ShowDialog() == true;
    }

    public static void Info(string message, string? title = null)
    {
        new MessageDialog(
            title ?? "PAV IT Inventory for SIDBI",
            message,
            Kind.Info,
            yesNo: false).ShowDialog();
    }

    public static void Warning(string message, string? title = null)
    {
        new MessageDialog(
            title ?? "PAV IT Inventory for SIDBI",
            message,
            Kind.Warning,
            yesNo: false).ShowDialog();
    }
}
