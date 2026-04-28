using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

public sealed class Pbkdf2KeyDerivationServiceTests
{
    private static readonly byte[] Salt = new byte[]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
    };

    [Fact]
    public void DeriveKey_SamePassphraseAndSalt_ProducesSameKey()
    {
        var svc = new Pbkdf2KeyDerivationService(iterations: 100_000);

        var k1 = svc.DeriveKey("hunter2", Salt);
        var k2 = svc.DeriveKey("hunter2", Salt);

        Assert.Equal(k1, k2);
        Assert.Equal(32, k1.Length);
    }

    [Fact]
    public void DeriveKey_DifferentPassphrase_ProducesDifferentKey()
    {
        var svc = new Pbkdf2KeyDerivationService(iterations: 100_000);

        var k1 = svc.DeriveKey("hunter2", Salt);
        var k2 = svc.DeriveKey("hunter3", Salt);

        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void DeriveKey_DifferentSalt_ProducesDifferentKey()
    {
        var svc = new Pbkdf2KeyDerivationService(iterations: 100_000);
        var altSalt = (byte[])Salt.Clone();
        altSalt[0] ^= 0xFF;

        var k1 = svc.DeriveKey("hunter2", Salt);
        var k2 = svc.DeriveKey("hunter2", altSalt);

        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void DeriveKey_NullPassphrase_Throws()
    {
        var svc = new Pbkdf2KeyDerivationService(iterations: 100_000);
        Assert.Throws<ArgumentNullException>(() => svc.DeriveKey(null!, Salt));
    }

    [Fact]
    public void DeriveKey_ShortSalt_Throws()
    {
        var svc = new Pbkdf2KeyDerivationService(iterations: 100_000);
        Assert.Throws<ArgumentException>(() => svc.DeriveKey("pw", new byte[8]));
    }

    [Fact]
    public void Constructor_TooFewIterations_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pbkdf2KeyDerivationService(1000));
    }
}
