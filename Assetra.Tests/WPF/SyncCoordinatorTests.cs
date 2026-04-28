using System.IO;
using System.Net;
using System.Net.Http;
using Moq;
using Assetra.Application.Sync;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using Xunit;

namespace Assetra.Tests.WPF;

public class SyncCoordinatorTests : IDisposable
{
    private readonly string _metaPath;

    public SyncCoordinatorTests()
    {
        _metaPath = Path.Combine(Path.GetTempPath(), $"sync-meta-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_metaPath)) File.Delete(_metaPath);
    }

    private SyncCoordinator CreateCoordinator(IAppSettingsService settings) =>
        new(settings,
            new CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object),
            new Mock<IConflictResolver>().Object,
            _metaPath);

    [Fact]
    public async Task SyncAsync_Throws_WhenSyncDisabled()
    {
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.Current).Returns(new AppSettings(SyncEnabled: false));
        var coord = CreateCoordinator(mock.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coord.SyncAsync("pass"));
    }

    [Fact]
    public async Task SyncAsync_Throws_WhenBackendUrlEmpty()
    {
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.Current).Returns(new AppSettings(SyncEnabled: true, SyncBackendUrl: ""));
        var coord = CreateCoordinator(mock.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coord.SyncAsync("pass"));
    }

    [Fact]
    public async Task SyncAsync_Throws_WhenPassphraseEmpty()
    {
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.Current).Returns(new AppSettings(SyncEnabled: true, SyncBackendUrl: "https://x"));
        var coord = CreateCoordinator(mock.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => coord.SyncAsync(""));
    }

    [Fact]
    public async Task SyncAsync_GeneratesDeviceIdAndSalt_OnFirstRun()
    {
        var current = new AppSettings(
            SyncEnabled: true,
            SyncBackendUrl: "https://nonexistent.invalid",
            SyncDeviceId: "",
            SyncPassphraseSalt: "");
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.Current).Returns(current);
        AppSettings? saved = null;
        mock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(s => saved = s)
            .Returns(Task.CompletedTask);

        var coord = CreateCoordinator(mock.Object);

        // Network call will fail but the bootstrap should run before it.
        try { await coord.SyncAsync("pass"); } catch { /* expected — invalid backend */ }

        Assert.NotNull(saved);
        Assert.False(string.IsNullOrEmpty(saved!.SyncDeviceId));
        Assert.False(string.IsNullOrEmpty(saved.SyncPassphraseSalt));
        Assert.True(Guid.TryParse(saved.SyncDeviceId, out _));
        var saltBytes = Convert.FromBase64String(saved.SyncPassphraseSalt);
        Assert.Equal(16, saltBytes.Length);
    }

    [Fact]
    public async Task SyncAsync_DoesNotRegenerateDeviceId_WhenAlreadySet()
    {
        var existing = "11111111-2222-3333-4444-555555555555";
        var existingSalt = Convert.ToBase64String(new byte[16]);
        var current = new AppSettings(
            SyncEnabled: true,
            SyncBackendUrl: "https://nonexistent.invalid",
            SyncDeviceId: existing,
            SyncPassphraseSalt: existingSalt);
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.Current).Returns(current);
        var saveCalls = 0;
        mock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback(() => saveCalls++)
            .Returns(Task.CompletedTask);

        var coord = CreateCoordinator(mock.Object);
        try { await coord.SyncAsync("pass"); } catch { /* expected */ }

        Assert.Equal(0, saveCalls);
    }

    [Fact]
    public async Task SyncAsync_PropagatesHttp5xx_FromBackend()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var current = new AppSettings(
            SyncEnabled: true,
            SyncBackendUrl: "https://sync.example.test",
            SyncDeviceId: "11111111-2222-3333-4444-555555555555",
            SyncPassphraseSalt: Convert.ToBase64String(new byte[16]));
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.Current).Returns(current);
        mock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var coord = new SyncCoordinator(
            mock.Object,
            new CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object),
            new Mock<IConflictResolver>().Object,
            _metaPath,
            httpClientFactory: () => new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => coord.SyncAsync("pass"));
    }

    [Fact]
    public async Task SyncAsync_HonorsCancellation()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var current = new AppSettings(
            SyncEnabled: true,
            SyncBackendUrl: "https://sync.example.test",
            SyncDeviceId: "11111111-2222-3333-4444-555555555555",
            SyncPassphraseSalt: Convert.ToBase64String(new byte[16]));
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.Current).Returns(current);
        mock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var coord = new SyncCoordinator(
            mock.Object,
            new CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object),
            new Mock<IConflictResolver>().Object,
            _metaPath,
            httpClientFactory: () => new HttpClient(handler));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coord.SyncAsync("pass", cts.Token));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((req, _) => Task.FromResult(responder(req)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request, cancellationToken);
    }
}
