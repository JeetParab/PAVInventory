using System.Windows;
using PAV.Shared.Enums;

namespace PAV.Client.Views;

public partial class BulkStatusWindow : Window
{
    public string? ChosenStatus { get; private set; }

    public BulkStatusWindow(int count)
    {
        InitializeComponent();
        Hint.Text = $"Set status on {count} selected assets.";
        StatusBox.ItemsSource = AssetStatusNames.All.Select(s => s.Display()).ToList();
        StatusBox.SelectedItem = AssetStatus.InUse.Display();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        ChosenStatus = StatusBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(ChosenStatus))
        {
            ErrorText.Text = "Pick a status.";
            return;
        }
        DialogResult = true;
    }
}
