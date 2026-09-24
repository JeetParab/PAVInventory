using System.Collections;

namespace PAV.Client.Views;

public partial class AssigneeBox : UserControl
{
    public AssigneeBox()
    {
        InitializeComponent();
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(AssigneeBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(AssigneeBox));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(AssigneeBox),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public void FocusBox()
    {
        Box.Focus();
        Box.CaretIndex = Box.Text?.Length ?? 0;
    }

    private void OnBoxKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (!IsOpen) IsOpen = true;
            if (List.Items.Count > 0)
            {
                List.Focus();
                List.SelectedIndex = Math.Max(0, List.SelectedIndex);
                var item = (ListBoxItem)List.ItemContainerGenerator.ContainerFromIndex(List.SelectedIndex);
                item?.Focus();
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && IsOpen)
        {
            IsOpen = false;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && IsOpen)
        {
            if (List.SelectedItem is string picked)
            {
                Pick(picked);
                e.Handled = true;
            }
            else
            {
                IsOpen = false;
            }
            return;
        }
    }

    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Pick(List.SelectedItem as string);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            IsOpen = false;
            Box.Focus();
            e.Handled = true;
        }
    }

    private void OnListClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is string name)
            Pick(name);
    }

    private void OnBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (IsKeyboardFocusWithin || List.IsKeyboardFocusWithin || List.IsMouseOver || Pop.IsMouseOver)
            return;
        IsOpen = false;
    }

    private void Pick(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Text = name.Trim();
        IsOpen = false;
        Box.Focus();
        Box.CaretIndex = Text.Length;
    }
}
