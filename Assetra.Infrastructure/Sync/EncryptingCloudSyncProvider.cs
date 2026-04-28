using System.Text;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// 把 plaintext envelopes 套上 AES-GCM 加密後再丟到 inner <see cref="ICloudSyncProvider"/>，
/// 反向則 pull → 解密 → 還原 plaintext envelope。
/// <para>
/// **加密範圍：僅 <see cref="SyncEnvelope.PayloadJson"/>**。
/// `EntityId` / `EntityType` / `Version` / `Deleted` 維持明文，因為：
/// (1) 後端需要它們做索引、cursor 排序、conflict 偵測；
/// (2) 它們本身不含敏感金額或對手方資訊。
/// </para>
/// <para>
/// 包裝格式：`base64(nonce ‖ tag ‖ ciphertext)`，固定前 12 bytes nonce、後 16 bytes 接 tag、其餘 ciphertext。
/// 將格式封閉在這個 class 內，其他層不需要知道 wire format。
/// </para>
/// <para>
/// Conflict 從 inner provider 回傳時，<see cref="SyncConflict.Remote"/> 的 PayloadJson 仍為密文 —
/// 解密一併處理，呼叫端 (resolver / UI) 看到的永遠是 plaintext。
/// </para>
/// </summary>
public sealed class EncryptingCloudSyncProvider : ICloudSyncProvider
{
    private readonly ICloudSyncProvider _inner;
    private readonly IEncryptionService _encryption;

    public EncryptingCloudSyncProvider(ICloudSyncProvider inner, IEncryptionService encryption)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(encryption);
        _inner = inner;
        _encryption = encryption;
    }

    public async Task<SyncPullResult> PullAsync(SyncMetadata metadata, CancellationToken ct = default)
    {
        var encrypted = await _inner.PullAsync(metadata, ct).ConfigureAwait(false);
        var decrypted = encrypted.Envelopes.Select(DecryptEnvelope).ToList();
        return new SyncPullResult(decrypted, encrypted.NextCursor);
    }

    public async Task<SyncPushResult> PushAsync(
        SyncMetadata metadata,
        IReadOnlyList<SyncEnvelope> envelopes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        var encrypted = envelopes.Select(EncryptEnvelope).ToList();
        var result = await _inner.PushAsync(metadata, encrypted, ct).ConfigureAwait(false);

        var decryptedConflicts = result.Conflicts
            .Select(c => new SyncConflict(DecryptEnvelope(c.Local), DecryptEnvelope(c.Remote)))
            .ToList();

        return new SyncPushResult(result.Accepted, decryptedConflicts, result.NextCursor);
    }

    private SyncEnvelope EncryptEnvelope(SyncEnvelope plain)
    {
        if (plain.Deleted && plain.PayloadJson.Length == 0)
            return plain;

        var bytes = Encoding.UTF8.GetBytes(plain.PayloadJson);
        var ep = _encryption.Encrypt(bytes);

        var packed = new byte[ep.Nonce.Length + ep.Tag.Length + ep.Ciphertext.Length];
        ep.Nonce.Span.CopyTo(packed.AsSpan(0, ep.Nonce.Length));
        ep.Tag.Span.CopyTo(packed.AsSpan(ep.Nonce.Length, ep.Tag.Length));
        ep.Ciphertext.Span.CopyTo(packed.AsSpan(ep.Nonce.Length + ep.Tag.Length));

        return plain with { PayloadJson = Convert.ToBase64String(packed) };
    }

    private SyncEnvelope DecryptEnvelope(SyncEnvelope cipher)
    {
        if (cipher.Deleted && cipher.PayloadJson.Length == 0)
            return cipher;

        var packed = Convert.FromBase64String(cipher.PayloadJson);
        const int NonceLen = AesGcmEncryptionService.NonceSizeBytes;
        const int TagLen = AesGcmEncryptionService.TagSizeBytes;

        if (packed.Length < NonceLen + TagLen)
            throw new System.Security.Cryptography.CryptographicException("Encrypted payload too short.");

        var nonce = packed.AsMemory(0, NonceLen);
        var tag = packed.AsMemory(NonceLen, TagLen);
        var ciphertext = packed.AsMemory(NonceLen + TagLen);

        var plain = _encryption.Decrypt(new EncryptedPayload(nonce, ciphertext, tag));
        return cipher with { PayloadJson = Encoding.UTF8.GetString(plain) };
    }
}
