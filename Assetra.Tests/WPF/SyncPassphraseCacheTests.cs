using Assetra.WPF.Infrastructure;
using Xunit;

namespace Assetra.Tests.WPF;

public class SyncPassphraseCacheTests
{
    [Fact]
    public void TryGet_ReturnsFalse_WhenEmpty()
    {
        var cache = new SyncPassphraseCache();
        Assert.False(cache.TryGet(out var p));
        Assert.Equal(string.Empty, p);
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsValue()
    {
        var cache = new SyncPassphraseCache();
        cache.Set("hunter2");
        Assert.True(cache.TryGet(out var p));
        Assert.Equal("hunter2", p);
    }

    [Fact]
    public void Clear_RemovesValue()
    {
        var cache = new SyncPassphraseCache();
        cache.Set("hunter2");
        cache.Clear();
        Assert.False(cache.TryGet(out _));
    }

    [Fact]
    public void Set_ThrowsOnEmpty()
    {
        var cache = new SyncPassphraseCache();
        Assert.Throws<ArgumentException>(() => cache.Set(""));
    }

    [Fact]
    public void Set_OverwritesPreviousValue()
    {
        var cache = new SyncPassphraseCache();
        cache.Set("first");
        cache.Set("second");
        Assert.True(cache.TryGet(out var p));
        Assert.Equal("second", p);
    }
}
