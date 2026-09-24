using PAV.Client.ViewModels;

namespace PAV.Client.Views;

public partial class PersonEditWindow : Window
{
    public PersonEditWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PersonEditViewModel vm)
                vm.CloseRequested += r =>
                {
                    try { DialogResult = r; } catch { }
                    Close();
                };
        };
    }
}
