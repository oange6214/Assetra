using System.IO;
using Moq;
using Assetra.Application.Sync;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.WPF.Features.Settings;
using Assetra.WPF.Infrastructure;
using Xunit;

namespace Assetra.Tests.WPF;

public class SyncSettingsViewModelTests
{
    private readonly Mock<IAppSettingsService> _settings = new();

    public SyncSettingsViewModelTests()
    {
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
    }

    private SyncCoordinator CreateCoordinator() =>
        new(_settings.Object,
            new CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object),
            new Mock<IConflictResolver>().Object,
            Path.Combine(Path.GetTempPath(), $"sync-meta-{Guid.NewGuid():N}.json"));

    private SyncSettingsViewModel CreateVm(
        SyncCoordinator? coord = null,
        SyncPassphraseCache? passphraseCache = null) =>
        new(_settings.Object, coord ?? CreateCoordinator(), passphraseCache ?? new SyncPassphraseCache());

    [Fact]
    public void Reload_PopulatesFromAppSettings()
    {
        _settings.Setup(s => s.Current).Returns(new AppSettings(
            SyncEnabled: true,
            SyncBackendUrl: "https://example.com",
            SyncAuthToken: "token-abc",
            SyncDeviceId: "device-1"));

        var vm = CreateVm();

        Assert.True(vm.Enabled);
        Assert.Equal("https://example.com", vm.BackendUrl);
        Assert.Equal("token-abc", vm.AuthToken);
        Assert.Equal("device-1", vm.DeviceId);
        Assert.Equal(string.Empty, vm.Passphrase);
    }

    [Fact]
    public void SyncNowCommand_CannotExecute_WhenPassphraseEmpty()
    {
        _settings.Setup(s => s.Current).Returns(new AppSettings());
        var vm = CreateVm();

        Assert.False(vm.SyncNowCommand.CanExecute(null));
    }

    [Fact]
    public void SyncNowCommand_CanExecute_WhenPassphraseProvidedAndNotSyncing()
    {
        _settings.Setup(s => s.Current).Returns(new AppSettings());
        var vm = CreateVm();
        vm.Passphrase = "secret";

        Assert.True(vm.SyncNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveSettingsCommand_PersistsEnabledBackendAndToken()
    {
        _settings.Setup(s => s.Current).Returns(new AppSettings());
        AppSettings? saved = null;
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(s => saved = s)
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Enabled = true;
        vm.BackendUrl = "  https://x  ";
        vm.AuthToken = "  tok  ";

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.True(saved!.SyncEnabled);
        Assert.Equal("https://x", saved.SyncBackendUrl);
        Assert.Equal("tok", saved.SyncAuthToken);
    }

    [Fact]
    public async Task SaveSettingsCommand_WhenBackgroundCacheDisabled_ClearsCachedPassphrase()
    {
        _settings.Setup(s => s.Current).Returns(new AppSettings());
        var cache = new SyncPassphraseCache();
        cache.Set("secret");
        var vm = CreateVm(passphraseCache: cache);

        vm.CachePassphraseForBackground = false;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.False(cache.TryGet(out _));
    }
}
