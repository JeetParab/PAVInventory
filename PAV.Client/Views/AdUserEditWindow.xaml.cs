using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class AdUserEditWindow : Window
{
    public AdUserEditWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AdUserEditViewModel vm)
                vm.CloseRequested += r =>
                {
                    try { DialogResult = r; } catch { }
                    Close();
                };
        };
    }
}
