using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Tests;

public sealed class CentralBranchCredentialModelTests
{
    [Fact]
    public void CredentialStoresOnlyAuthenticatedCiphertextForAnExistingInstallation()
    {
        var installationId = Guid.NewGuid();
        var encrypted = new CentralEncryptedBranchCredential(1,
            new byte[12], new byte[32], new byte[16]);
        var credential = CentralBranchCredential.Create(installationId, encrypted);

        Assert.Equal(installationId, credential.InstallationId);
        Assert.Equal(1, credential.KeyVersion);
        Assert.DoesNotContain(typeof(CentralBranchCredential).GetProperties(),
            property => property.Name is "Token" or "Secret" or "Plaintext");

        var options = new DbContextOptionsBuilder<CentralAccessDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres")
            .Options;
        using var context = new CentralAccessDbContext(options);
        var entity = context.Model.FindEntityType(typeof(CentralBranchCredential))!;
        Assert.Equal("central_access", entity.GetSchema());
        Assert.Equal("branch_credentials", entity.GetTableName());
        Assert.Equal(nameof(CentralBranchCredential.InstallationId),
            Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(typeof(CentralInstallation), Assert.Single(entity.GetForeignKeys()).PrincipalEntityType.ClrType);
    }

    [Fact]
    public void MalformedEnvelopeCannotBeStored()
    {
        Assert.Throws<ArgumentException>(() => CentralBranchCredential.Create(Guid.Empty,
            new CentralEncryptedBranchCredential(1, new byte[12], new byte[32], new byte[16])));
        Assert.Throws<ArgumentException>(() => CentralBranchCredential.Create(Guid.NewGuid(),
            new CentralEncryptedBranchCredential(0, new byte[12], new byte[32], new byte[16])));
    }
}
