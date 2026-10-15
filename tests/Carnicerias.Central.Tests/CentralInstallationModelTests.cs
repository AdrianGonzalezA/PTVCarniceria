using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Tests;

public sealed class CentralInstallationModelTests
{
    [Fact]
    public void RegistryUsesIndependentCentralAndRemoteCompanyIdentifiers()
    {
        var centralCompanyId = Guid.NewGuid();
        var remoteCompanyId = Guid.NewGuid();
        var installation = CentralInstallation.Create(
            Guid.NewGuid(), centralCompanyId, remoteCompanyId, Guid.NewGuid(),
            "Sucursal Centro", "https://branch.internal.example/", "branch-centro-token");

        Assert.NotEqual(installation.CentralCompanyId, installation.RemoteCompanyId);
        Assert.Equal(centralCompanyId, installation.CentralCompanyId);
        Assert.Equal(remoteCompanyId, installation.RemoteCompanyId);
        Assert.True(installation.IsActive);
        Assert.DoesNotContain(typeof(CentralInstallation).GetProperties(),
            property => property.Name is "Token" or "Secret" or "IsConnected" or "SalesTotal");
    }

    [Fact]
    public void InstallationIdIsTheGlobalKeyAndAllTablesStayInCentralSchema()
    {
        var options = new DbContextOptionsBuilder<CentralAccessDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres")
            .Options;
        using var context = new CentralAccessDbContext(options);
        var entity = context.Model.FindEntityType(typeof(CentralInstallation))!;

        Assert.Equal("central_access", entity.GetSchema());
        Assert.Equal("installations", entity.GetTableName());
        Assert.Equal(nameof(CentralInstallation.InstallationId),
            Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.All(context.Model.GetEntityTypes(), item => Assert.Equal("central_access", item.GetSchema()));
    }
}
