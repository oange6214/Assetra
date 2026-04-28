using System.IO;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

public class JsonSyncMetadataStoreTests : IDisposable
{
    private readonly string _path;

    public JsonSyncMetadataStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"sync-meta-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task GetAsync_ReturnsEmptyWithDefaultDeviceId_WhenFileMissing()
    {
        using var store = new JsonSyncMetadataStore(_path, "device-A");
        var meta = await store.GetAsync();
        Assert.Equal("device-A", meta.DeviceId);
        Assert.Null(meta.LastSyncAt);
        Assert.Null(meta.Cursor);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTrips()
    {
        using var store = new JsonSyncMetadataStore(_path, "device-A");
        var when = DateTimeOffset.Parse("2026-04-28T10:00:00Z");
        await store.SaveAsync(new SyncMetadata("device-B", when, "cursor-42"));

        var meta = await store.GetAsync();
        Assert.Equal("device-B", meta.DeviceId);
        Assert.Equal(when, meta.LastSyncAt);
        Assert.Equal("cursor-42", meta.Cursor);
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingFile()
    {
        using var store = new JsonSyncMetadataStore(_path, "device-A");
        await store.SaveAsync(new SyncMetadata("d1", DateTimeOffset.UnixEpoch, "c1"));
        await store.SaveAsync(new SyncMetadata("d2", DateTimeOffset.UnixEpoch.AddHours(1), "c2"));

        var meta = await store.GetAsync();
        Assert.Equal("d2", meta.DeviceId);
        Assert.Equal("c2", meta.Cursor);
    }

    [Fact]
    public async Task SaveAsync_DoesNotLeaveTempFileBehind()
    {
        using var store = new JsonSyncMetadataStore(_path, "device-A");
        await store.SaveAsync(new SyncMetadata("d1", null, null));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => new JsonSyncMetadataStore("", "device"));
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyDeviceId()
    {
        Assert.Throws<ArgumentException>(() => new JsonSyncMetadataStore(_path, ""));
    }
}
