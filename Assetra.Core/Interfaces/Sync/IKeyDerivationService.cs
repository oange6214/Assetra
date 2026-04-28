namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// 把使用者輸入的密語轉成固定長度（建議 32 bytes）對稱金鑰。
/// 實作必須使用 memory-hard 或高 iteration 的 KDF（PBKDF2 / Argon2id / scrypt），
/// 且同一 (passphrase, salt) 必須產出相同輸出（idempotent）。
/// </summary>
public interface IKeyDerivationService
{
    /// <summary>建議長度 32 bytes（AES-256）。</summary>
    byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int keyLengthBytes = 32);
}
