namespace Assetra.Core.Models.Sync;

/// <summary>
/// AES-GCM 加密產出三元組：nonce（IV）+ ciphertext + auth tag。
/// 各欄皆為 raw bytes；序列化（base64 / hex / binary）由上層決定。
/// </summary>
/// <param name="Nonce">96-bit IV，每次加密必須唯一（同一把 key 下重用 = 災難）。</param>
/// <param name="Ciphertext">與 plaintext 同長度的密文。</param>
/// <param name="Tag">128-bit GCM auth tag，解密失敗時用來偵測竄改 / wrong key。</param>
public sealed record EncryptedPayload(
    ReadOnlyMemory<byte> Nonce,
    ReadOnlyMemory<byte> Ciphertext,
    ReadOnlyMemory<byte> Tag);
