using System.Security.Cryptography;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// AES-256-GCM 加密實作（BCL <see cref="AesGcm"/>，零 NuGet 相依）。
/// <para>
/// • Key 必須 32 bytes（AES-256）。
/// • Nonce 每次加密以 <see cref="RandomNumberGenerator"/> 隨機生成 12 bytes；同一 key 下不可重用。
/// • Tag 固定 16 bytes，<see cref="Decrypt"/> 在 tag 不符時 throw <see cref="CryptographicException"/>。
/// </para>
/// 為何不存 KDF salt 在這裡：salt 是 KDF 層概念，加密層只關心 key bytes 是否合法。
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService, IDisposable
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    private readonly AesGcm _aes;

    public AesGcmEncryptionService(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException(
                $"AES-256 key must be exactly {KeySizeBytes} bytes (got {key.Length}).",
                nameof(key));
        }
        _aes = new AesGcm(key, TagSizeBytes);
    }

    public EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];
        _aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return new EncryptedPayload(nonce, ciphertext, tag);
    }

    public byte[] Decrypt(EncryptedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Nonce.Length != NonceSizeBytes)
            throw new CryptographicException($"Invalid nonce length: {payload.Nonce.Length}");
        if (payload.Tag.Length != TagSizeBytes)
            throw new CryptographicException($"Invalid tag length: {payload.Tag.Length}");

        var plaintext = new byte[payload.Ciphertext.Length];
        _aes.Decrypt(payload.Nonce.Span, payload.Ciphertext.Span, payload.Tag.Span, plaintext);
        return plaintext;
    }

    public void Dispose() => _aes.Dispose();
}
