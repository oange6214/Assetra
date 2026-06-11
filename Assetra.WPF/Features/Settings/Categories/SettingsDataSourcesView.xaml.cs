using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Settings.Categories;

public partial class SettingsDataSourcesView : UserControl
{
    public SettingsDataSourcesView() => InitializeComponent();

    // PasswordBox.Password is not a DependencyProperty, so it cannot be bound;
    // seed it from the VM on load and push edits back via PasswordChanged.
    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            if (FugleApiKeyBox.Password != vm.FugleApiKey)
                FugleApiKeyBox.Password = vm.FugleApiKey;
            if (TwelveDataApiKeyBox.Password != vm.TwelveDataApiKey)
                TwelveDataApiKeyBox.Password = vm.TwelveDataApiKey;
        }
    }

    private void FugleApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox box && vm.FugleApiKey != box.Password)
            vm.FugleApiKey = box.Password;
    }

    private void TwelveDataApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox box && vm.TwelveDataApiKey != box.Password)
            vm.TwelveDataApiKey = box.Password;
    }
}
