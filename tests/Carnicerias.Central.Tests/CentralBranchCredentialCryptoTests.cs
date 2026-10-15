using System.Security.Cryptography;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralBranchCredentialCryptoTests
{
    private const string Token = "8E6F18C76C7564D3BB7D464D7B48A8BA216ED6F0BA3705D964721079048D18CB";

    [Fact]
    public void RoundTripsWithCompanyAndInstallationAsAssociatedData()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var crypto = new CentralBranchCredentialCrypto(2, version => version == 2 ? key : null);
        var companyId = Guid.NewGuid();
        var installationId = Guid.NewGuid();

        var encrypted = crypto.Encrypt(companyId, installationId, Token);

        Assert.Equal(2, encrypted.KeyVersion);
        Assert.Equal(12, encrypted.Nonce.Length);
        Assert.Equal(16, encrypted.Tag.Length);
        Assert.DoesNotContain(Token, Convert.ToHexString(encrypted.Ciphertext), StringComparison.Ordinal);
        Assert.Equal(Token, crypto.Decrypt(companyId, installationId, encrypted));
        Assert.Throws<InvalidOperationException>(() =>
            crypto.Decrypt(Guid.NewGuid(), installationId, encrypted));
        Assert.Throws<InvalidOperationException>(() =>
            crypto.Decrypt(companyId, Guid.NewGuid(), encrypted));
    }

    [Fact]
    public void TamperingMissingKeyAndMalformedTokensFailClosed()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var crypto = new CentralBranchCredentialCrypto(1, version => version == 1 ? key : null);
        var companyId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var encrypted = crypto.Encrypt(companyId, installationId, Token);
        encrypted.Ciphertext[0] ^= 0x01;

        var error = Assert.Throws<InvalidOperationException>(() =>
            crypto.Decrypt(companyId, installationId, encrypted));
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            crypto.Decrypt(companyId, installationId, encrypted with { KeyVersion = 9 }));
        Assert.Throws<InvalidOperationException>(() =>
            crypto.Encrypt(companyId, installationId, "not-a-token"));
        Assert.Throws<InvalidOperationException>(() =>
            new CentralBranchCredentialCrypto(1, _ => null)
                .Encrypt(companyId, installationId, Token));
    }

    [Fact]
    public void OldKeyRemainsReadableAfterSelectingNewActiveVersion()
    {
        var keys = new Dictionary<int, byte[]>
        {
            [1] = RandomNumberGenerator.GetBytes(32),
            [2] = RandomNumberGenerator.GetBytes(32)
        };
        var companyId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var oldCrypto = new CentralBranchCredentialCrypto(1, keys.GetValueOrDefault);
        var encrypted = oldCrypto.Encrypt(companyId, installationId, Token);

        var rotated = new CentralBranchCredentialCrypto(2, keys.GetValueOrDefault);
        Assert.Equal(Token, rotated.Decrypt(companyId, installationId, encrypted));
        Assert.Equal(2, rotated.Encrypt(companyId, installationId, Token).KeyVersion);
    }
}
