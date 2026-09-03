using System.Windows.Controls;
using System.Windows.Input;
using PAV.Client.Services;

namespace PAV.Client.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnNestedPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        PageScroll.Apply(RootScroll, e);
}
