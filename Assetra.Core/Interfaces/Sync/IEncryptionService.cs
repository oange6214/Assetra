using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// 對稱加密抽象。實作須使用 AEAD（AES-GCM / ChaCha20-Poly1305 等），
/// <see cref="Decrypt"/> 必須在密文 / tag 被竄改時 throw。
/// </summary>
public interface IEncryptionService
{
    EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext);
    byte[] Decrypt(EncryptedPayload payload);
}
