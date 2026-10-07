using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class PosTerminalProvisioningTests
{
    [PostgreSqlFact]
    public async Task CreatesTwoTerminalsWithDifferentCredentialsInOneBranch()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var databaseName = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database;
        Assert.StartsWith("carnicerias_test_", databaseName, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        try
        {
            await using var db = new PlatformAccessDbContext(options);
            await db.Database.MigrateAsync();
            var company = new Company("Empresa de prueba");
            var branch = new Branch(company.Id, "Sucursal de prueba");
            db.AddRange(company, branch);
            await db.SaveChangesAsync();

            var provisioning = new PosTerminalProvisioningService(db);
            var first = await provisioning.CreateAsync(branch.Id, "Caja 1");
            var second = await provisioning.CreateAsync(branch.Id, "Caja 2");

            Assert.NotEqual(first.Credential.Token, second.Credential.Token);
            Assert.Equal(company.Id, first.Terminal.CompanyId);
            Assert.Equal(branch.Id, first.Terminal.BranchId);
            Assert.Equal(2, await db.PosTerminals.CountAsync());
            Assert.DoesNotContain(first.Credential.Token,
                await db.PosTerminals.Select(terminal => terminal.CredentialHash).ToArrayAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => provisioning.CreateAsync(branch.Id, "Caja 1"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => provisioning.CreateAsync(Guid.NewGuid(), "Caja 3"));
        }
        finally
        {
            await using var cleanup = new PlatformAccessDbContext(options);
            await cleanup.Database.MigrateAsync("0");
        }
    }
}
