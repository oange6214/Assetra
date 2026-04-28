using System.Security.Cryptography;
using System.Text;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

public sealed class AesGcmEncryptionServiceTests
{
    private static byte[] DeterministicKey()
    {
        var key = new byte[32];
        for (int i = 0; i < key.Length; i++) key[i] = (byte)i;
        return key;
    }

    [Fact]
    public void Roundtrip_ReturnsOriginalPlaintext()
    {
        using var svc = new AesGcmEncryptionService(DeterministicKey());
        var plaintext = Encoding.UTF8.GetBytes("hello sync");

        var encrypted = svc.Encrypt(plaintext);
        var decrypted = svc.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_NonceIsRandom_TwoCallsProduceDifferentNonces()
    {
        using var svc = new AesGcmEncryptionService(DeterministicKey());
        var pt = Encoding.UTF8.GetBytes("same plaintext");

        var a = svc.Encrypt(pt);
        var b = svc.Encrypt(pt);

        Assert.NotEqual(a.Nonce.ToArray(), b.Nonce.ToArray());
        Assert.NotEqual(a.Ciphertext.ToArray(), b.Ciphertext.ToArray());
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        using var svc = new AesGcmEncryptionService(DeterministicKey());
        var encrypted = svc.Encrypt(Encoding.UTF8.GetBytes("payload"));

        var bad = encrypted.Ciphertext.ToArray();
        bad[0] ^= 0xFF;
        var tampered = new EncryptedPayload(encrypted.Nonce, bad, encrypted.Tag);

        Assert.ThrowsAny<CryptographicException>(() => svc.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var pt = Encoding.UTF8.GetBytes("secret");
        EncryptedPayload encrypted;
        using (var svc = new AesGcmEncryptionService(DeterministicKey()))
            encrypted = svc.Encrypt(pt);

        var wrongKey = new byte[32];
        wrongKey[0] = 0x99;
        using var bad = new AesGcmEncryptionService(wrongKey);

        Assert.ThrowsAny<CryptographicException>(() => bad.Decrypt(encrypted));
    }

    [Fact]
    public void Constructor_WrongKeyLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptionService(new byte[16]));
    }

    [Fact]
    public void Decrypt_BadNonceLength_Throws()
    {
        using var svc = new AesGcmEncryptionService(DeterministicKey());
        var encrypted = svc.Encrypt(Encoding.UTF8.GetBytes("x"));
        var bad = new EncryptedPayload(new byte[8], encrypted.Ciphertext, encrypted.Tag);
        Assert.ThrowsAny<CryptographicException>(() => svc.Decrypt(bad));
    }
}
