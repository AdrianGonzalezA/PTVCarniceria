using System.Net;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class PosTerminalEndpointTests
{
    [PostgreSqlFact]
    public async Task CurrentTerminalRequiresAnActiveCredentialAndNeverReturnsIt()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var databaseName = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database;
        Assert.StartsWith("carnicerias_test_", databaseName, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var issued = PosTerminalCredential.Issue();
        Guid terminalId;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                await db.Database.MigrateAsync();
                var company = new Company("Empresa terminal");
                var branch = new Branch(company.Id, "Centro");
                var terminal = new PosTerminal(company.Id, branch.Id, "Caja 1");
                terminal.AssignCredentialHash(issued.Hash);
                terminalId = terminal.Id;
                db.AddRange(company, branch, terminal);
                await db.SaveChangesAsync();
            }

            using var factory = new TerminalApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/pos-terminals/current")).StatusCode);

            using var invalid = new HttpRequestMessage(HttpMethod.Get, "/api/pos-terminals/current");
            invalid.Headers.Add("X-Pos-Terminal-Credential", PosTerminalCredential.Issue().Token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(invalid)).StatusCode);

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/pos-terminals/current");
            request.Headers.Add("X-Pos-Terminal-Credential", issued.Token);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(terminalId.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Caja 1", body, StringComparison.Ordinal);
            Assert.DoesNotContain(issued.Token, body, StringComparison.Ordinal);
            Assert.DoesNotContain(issued.Hash, body, StringComparison.Ordinal);

            await using (var db = new PlatformAccessDbContext(options))
            {
                var terminal = await db.PosTerminals.SingleAsync();
                terminal.Deactivate();
                await db.SaveChangesAsync();
            }
            using var revoked = new HttpRequestMessage(HttpMethod.Get, "/api/pos-terminals/current");
            revoked.Headers.Add("X-Pos-Terminal-Credential", issued.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(revoked)).StatusCode);
        }
        finally
        {
            await using var cleanup = new PlatformAccessDbContext(options);
            await cleanup.Database.MigrateAsync("0");
        }
    }

    private sealed class TerminalApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PlatformAccess"] = connectionString
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PlatformAccessDbContext>();
                services.RemoveAll<DbContextOptions<PlatformAccessDbContext>>();
                services.AddDbContext<PlatformAccessDbContext>(options => options.UseNpgsql(connectionString));
            });
        }
    }
}
