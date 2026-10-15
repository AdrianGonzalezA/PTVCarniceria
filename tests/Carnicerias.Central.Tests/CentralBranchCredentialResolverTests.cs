using System.Security.Cryptography;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralBranchCredentialResolverTests
{
    private const string Token = "A4F18C954BE7764187601D518CAC669A45158486E40B66FAE807748E99C4681A";

    [Fact]
    public async Task ManagedCredentialIsDecryptedWithoutReadingLegacyEnvironment()
    {
        var installationId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var crypto = Crypto();
        var encrypted = crypto.Encrypt(companyId, installationId, Token);
        var reader = new FakeReader(encrypted);
        var legacy = new FakeLegacySource(Token);
        var installation = Installation(companyId, installationId,
            CentralManagedSecretReference.Create(installationId));
        var resolver = new CentralBranchCredentialResolver(reader, legacy, () => crypto);

        Assert.Equal(Token, await resolver.ResolveAsync(installation, CancellationToken.None));
        Assert.Equal(1, reader.Calls);
        Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task MissingOrCorruptManagedCredentialNeverFallsBackToLegacySource()
    {
        var installationId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var crypto = Crypto();
        var legacy = new FakeLegacySource(Token);
        var installation = Installation(companyId, installationId,
            CentralManagedSecretReference.Create(installationId));
        var missing = new CentralBranchCredentialResolver(new FakeReader(null), legacy, () => crypto);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            missing.ResolveAsync(installation, CancellationToken.None));

        var encrypted = crypto.Encrypt(companyId, installationId, Token);
        encrypted.Tag[0] ^= 0x01;
        var corrupt = new CentralBranchCredentialResolver(new FakeReader(encrypted), legacy,
            () => crypto);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            corrupt.ResolveAsync(installation, CancellationToken.None));
        Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task ExistingCliReferenceKeepsEnvironmentSource()
    {
        var reader = new FakeReader(null);
        var legacy = new FakeLegacySource(Token);
        var installation = Installation(Guid.NewGuid(), Guid.NewGuid(), "VISUAL_NORTE");
        var resolver = new CentralBranchCredentialResolver(reader, legacy,
            () => throw new InvalidOperationException("Managed key should not be read."));

        Assert.Equal(Token, await resolver.ResolveAsync(installation, CancellationToken.None));
        Assert.Equal(0, reader.Calls);
        Assert.Equal(1, legacy.Calls);
        Assert.Throws<InvalidOperationException>(() => CentralManagedSecretReference.IsManaged(
            Installation(Guid.NewGuid(), Guid.NewGuid(), "DB_INCORRECTO")));
    }

    private static CentralBranchCredentialCrypto Crypto()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        return new CentralBranchCredentialCrypto(1, version => version == 1 ? key : null);
    }

    private static CentralInstallation Installation(Guid companyId, Guid installationId,
        string secretReference) => CentralInstallation.Create(installationId, companyId,
            Guid.NewGuid(), Guid.NewGuid(), "Sucursal", "https://branch.example/", secretReference);

    private sealed class FakeReader(CentralEncryptedBranchCredential? envelope)
        : ICentralEncryptedBranchCredentialReader
    {
        public int Calls { get; private set; }

        public Task<CentralEncryptedBranchCredential?> FindAsync(Guid installationId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(envelope);
        }
    }

    private sealed class FakeLegacySource(string? secret) : ICentralBranchCredentialSource
    {
        public int Calls { get; private set; }

        public string? Read(string reference)
        {
            Calls++;
            return secret;
        }
    }
}
