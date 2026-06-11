using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Settings.Categories;

public partial class SettingsSyncView : UserControl
{
    private SyncSettingsViewModel? _subscribedSyncVm;

    public SettingsSyncView() => InitializeComponent();

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            SubscribeToPassphraseCleared(vm.Sync);
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e) =>
        SubscribeToPassphraseCleared(null);

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
