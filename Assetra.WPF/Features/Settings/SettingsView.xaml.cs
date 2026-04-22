using System.Windows.Controls;
using System.Windows;

namespace Assetra.WPF.Features.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && FugleApiKeyBox.Password != vm.FugleApiKey)
            FugleApiKeyBox.Password = vm.FugleApiKey;
    }

    private void FugleApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox box && vm.FugleApiKey != box.Password)
            vm.FugleApiKey = box.Password;
    }
}
