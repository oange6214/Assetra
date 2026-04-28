using System.Security.Cryptography;
using Assetra.Core.Interfaces.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// PBKDF2-SHA256 KDF（BCL <see cref="Rfc2898DeriveBytes"/>，零 NuGet 相依）。
/// 預設 iteration 600,000 — OWASP 2023 對 PBKDF2-SHA256 建議下限。
/// <para>
/// ⚠ PBKDF2 不是 memory-hard，對 GPU / ASIC 暴力破解抗性不如 Argon2id / scrypt。
/// 後續若引入 <c>Konscious.Security.Cryptography.Argon2</c> NuGet 即可在不改介面的前提下換 KDF。
/// 本 sprint 選 PBKDF2 的理由：(1) BCL 內建、(2) 600k 對個人理財 app 的威脅模型已足夠、
/// (3) 等實際接後端再評估是否值得加 NuGet。
/// </para>
/// </summary>
public sealed class Pbkdf2KeyDerivationService : IKeyDerivationService
{
    public const int DefaultIterations = 600_000;

    private readonly int _iterations;

    public Pbkdf2KeyDerivationService(int iterations = DefaultIterations)
    {
        if (iterations < 100_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "PBKDF2 iterations must be at least 100,000.");
        _iterations = iterations;
    }

    public byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int keyLengthBytes = 32)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        if (salt.Length < 16)
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));
        if (keyLengthBytes is < 16 or > 64)
            throw new ArgumentOutOfRangeException(nameof(keyLengthBytes), "Key length must be 16–64 bytes.");

        return Rfc2898DeriveBytes.Pbkdf2(
            password: passphrase,
            salt: salt,
            iterations: _iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: keyLengthBytes);
    }
}
