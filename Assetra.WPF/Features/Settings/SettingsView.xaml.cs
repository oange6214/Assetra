using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Input;

namespace Assetra.WPF.Features.Settings;

public partial class SettingsView : UserControl
{
    private SyncSettingsViewModel? _subscribedSyncVm;

    public SettingsView() => InitializeComponent();

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            if (FugleApiKeyBox.Password != vm.FugleApiKey)
                FugleApiKeyBox.Password = vm.FugleApiKey;
            SubscribeToPassphraseCleared(vm.Sync);
        }
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e) =>
        SubscribeToPassphraseCleared(null);

    private void FugleApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox box && vm.FugleApiKey != box.Password)
            vm.FugleApiKey = box.Password;
    }

    private async void OnUiScaleSliderDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.CommitUiScaleAsync();
    }

    private async void OnUiScaleSliderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.CommitUiScaleAsync();
    }

    private void SyncPassphrase_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox box)
            vm.Sync.Passphrase = box.Password;
    }

    private void SubscribeToPassphraseCleared(SyncSettingsViewModel? syncVm)
    {
        if (ReferenceEquals(_subscribedSyncVm, syncVm))
            return;

        if (_subscribedSyncVm is not null)
            _subscribedSyncVm.PassphraseCleared -= OnPassphraseCleared;

        _subscribedSyncVm = syncVm;
        if (_subscribedSyncVm is not null)
            _subscribedSyncVm.PassphraseCleared += OnPassphraseCleared;
    }

    private void OnPassphraseCleared()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnPassphraseCleared);
            return;
        }

        if (!string.IsNullOrEmpty(SyncPassphraseBox.Password))
            SyncPassphraseBox.Clear();
    }
}
