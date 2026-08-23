using System.Windows;

namespace PAV.Client.Views;

public partial class AddLocationWindow : Window
{
    public string LocationName => NameBox.Text.Trim();
    public string? Description => string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text.Trim();

    public AddLocationWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Name is required.";
            return;
        }
        DialogResult = true;
    }
}
