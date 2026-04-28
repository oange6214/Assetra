using System.Security.Cryptography;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

public sealed class EncryptingCloudSyncProviderTests
{
    private static byte[] DeterministicKey()
    {
        var key = new byte[32];
        for (int i = 0; i < key.Length; i++) key[i] = (byte)(i + 1);
        return key;
    }

    private static SyncEnvelope MakeEnvelope(string payload, bool deleted = false) => new(
        EntityId: Guid.NewGuid(),
        EntityType: "Trade",
        PayloadJson: payload,
        Version: new EntityVersion(1, DateTimeOffset.UtcNow, "device-A"),
        Deleted: deleted);

    [Fact]
    public async Task PushThenPull_RoundTripsPlaintextThroughEncryption()
    {
        var inner = new InMemoryCloudSyncProvider();
        using var enc = new AesGcmEncryptionService(DeterministicKey());
        var provider = new EncryptingCloudSyncProvider(inner, enc);

        var envelope = MakeEnvelope("{\"amount\":-1234.56}");
        await provider.PushAsync(SyncMetadata.Empty("device-A"), new[] { envelope });

        var pull = await provider.PullAsync(SyncMetadata.Empty("device-B"));

        Assert.Single(pull.Envelopes);
        Assert.Equal("{\"amount\":-1234.56}", pull.Envelopes[0].PayloadJson);
        Assert.Equal(envelope.EntityId, pull.Envelopes[0].EntityId);
    }

    [Fact]
    public async Task InnerStore_NeverSeesPlaintext()
    {
        var inner = new InMemoryCloudSyncProvider();
        using var enc = new AesGcmEncryptionService(DeterministicKey());
        var provider = new EncryptingCloudSyncProvider(inner, enc);

        var envelope = MakeEnvelope("PLAINTEXT_SECRET");
        await provider.PushAsync(SyncMetadata.Empty("device-A"), new[] { envelope });

        var rawPull = await inner.PullAsync(SyncMetadata.Empty("device-X"));
        Assert.DoesNotContain("PLAINTEXT_SECRET", rawPull.Envelopes[0].PayloadJson);
    }

    [Fact]
    public async Task Decrypt_WithWrongKey_Throws()
    {
        var inner = new InMemoryCloudSyncProvider();
        using var encA = new AesGcmEncryptionService(DeterministicKey());
        var writer = new EncryptingCloudSyncProvider(inner, encA);

        await writer.PushAsync(SyncMetadata.Empty("d"), new[] { MakeEnvelope("payload") });

        var wrongKey = new byte[32];
        using var encB = new AesGcmEncryptionService(wrongKey);
        var reader = new EncryptingCloudSyncProvider(inner, encB);

        await Assert.ThrowsAnyAsync<CryptographicException>(() =>
            reader.PullAsync(SyncMetadata.Empty("d")));
    }

    [Fact]
    public async Task Push_TombstoneWithEmptyPayload_PassesThroughUnchanged()
    {
        var inner = new InMemoryCloudSyncProvider();
        using var enc = new AesGcmEncryptionService(DeterministicKey());
        var provider = new EncryptingCloudSyncProvider(inner, enc);

        var tombstone = MakeEnvelope(string.Empty, deleted: true);
        await provider.PushAsync(SyncMetadata.Empty("d"), new[] { tombstone });

        var rawPull = await inner.PullAsync(SyncMetadata.Empty("x"));
        Assert.Equal(string.Empty, rawPull.Envelopes[0].PayloadJson);
        Assert.True(rawPull.Envelopes[0].Deleted);

        var decryptedPull = await provider.PullAsync(SyncMetadata.Empty("y"));
        Assert.Equal(string.Empty, decryptedPull.Envelopes[0].PayloadJson);
    }

    [Fact]
    public async Task Push_StaleVersion_ConflictRemoteIsDecrypted()
    {
        var inner = new InMemoryCloudSyncProvider();
        using var enc = new AesGcmEncryptionService(DeterministicKey());
        var provider = new EncryptingCloudSyncProvider(inner, enc);

        var id = Guid.NewGuid();
        var t = DateTimeOffset.UtcNow;
        var first = new SyncEnvelope(id, "Trade", "{\"v\":1}",
            new EntityVersion(5, t, "d"));
        await provider.PushAsync(SyncMetadata.Empty("d"), new[] { first });

        var stale = new SyncEnvelope(id, "Trade", "{\"v\":0}",
            new EntityVersion(2, t, "d"));
        var result = await provider.PushAsync(SyncMetadata.Empty("d"), new[] { stale });

        Assert.Single(result.Conflicts);
        Assert.Equal("{\"v\":1}", result.Conflicts[0].Remote.PayloadJson);
        Assert.Equal("{\"v\":0}", result.Conflicts[0].Local.PayloadJson);
    }

    [Fact]
    public void Constructor_NullArgs_Throws()
    {
        using var enc = new AesGcmEncryptionService(DeterministicKey());
        Assert.Throws<ArgumentNullException>(() => new EncryptingCloudSyncProvider(null!, enc));
        Assert.Throws<ArgumentNullException>(() =>
            new EncryptingCloudSyncProvider(new InMemoryCloudSyncProvider(), null!));
    }
}
