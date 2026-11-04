using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Domain.Sales;
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
using Npgsql;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class CashierShiftPreviousVisualEndpointTests
{
    [I15ApprovedVisualWriteFact]
    [Trait("Category", "I15ApprovedDatabaseWrite")]
    public async Task PreviousCloseFollowsPhysicalTerminalAcrossCashiersAndNewFundIsIndependent()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I15 fixture {suffix}", $"I15-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I15");
        var role = new Role($"I15-{suffix}", "I15 cashier");
        var password = $"I15-{Guid.NewGuid():N}!";
        var hasher = new Argon2idPasswordHasher();
        var cashierA = UserIdentity.CreateForCompany(company.Id, $"i15a-{suffix}",
            $"i15a-{suffix}@example.test", hasher.Hash(password));
        var cashierB = UserIdentity.CreateForCompany(company.Id, $"i15b-{suffix}",
            $"i15b-{suffix}@example.test", hasher.Hash(password));
        var credentialA = PosTerminalCredential.Issue();
        var credentialB = PosTerminalCredential.Issue();
        var terminalA = new PosTerminal(company.Id, branch.Id, "Caja A I15");
        var terminalB = new PosTerminal(company.Id, branch.Id, "Caja B I15");
        terminalA.AssignCredentialHash(credentialA.Hash);
        terminalB.AssignCredentialHash(credentialB.Hash);
        var first = ClosedShift(cashierA.Id, terminalA.Id, 100m, 8);
        var previous = ClosedShift(cashierB.Id, terminalA.Id, 230m, 10);
        var otherTerminal = ClosedShift(cashierA.Id, terminalB.Id, 340m, 11);
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                db.AddRange(company, branch, role, cashierA, cashierB,
                    new UserAssignment(cashierA.Id, role.Id, company.Id),
                    new UserAssignment(cashierB.Id, role.Id, company.Id),
                    terminalA, terminalB, first, previous, otherTerminal);
                foreach (var shift in new[] { first, previous, otherTerminal })
                    db.CashLedgerMovements.Add(new CashLedgerMovement(company.Id,
                        branch.Id, shift.Id, shift.CashierId, shift.Id,
                        PaymentMethod.Cash, CashLedgerMovementKind.Opening,
                        shift.OpeningCash, shift.OpenedAtUtc,
                        posTerminalId: shift.PosTerminalId));
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var cashierAOnA = await LoginAsync(factory, company, branch,
                cashierA, password, credentialA.Token);
            using var cashierBOnA = await LoginAsync(factory, company, branch,
                cashierB, password, credentialA.Token);
            using var cashierAOnB = await LoginAsync(factory, company, branch,
                cashierA, password, credentialB.Token);

            var lastForA = await GetLastClosedAsync(cashierAOnA);
            Assert.Equal(previous.Id, lastForA.GetProperty("id").GetGuid());
            Assert.Equal(230m, lastForA.GetProperty("cashBalance").GetDecimal());
            Assert.Equal(cashierB.Username, lastForA.GetProperty("cashierName").GetString());
            Assert.Equal(previous.ClosedAtUtc,
                lastForA.GetProperty("closedAtUtc").GetDateTimeOffset());

            var lastForB = await GetLastClosedAsync(cashierBOnA);
            Assert.Equal(previous.Id, lastForB.GetProperty("id").GetGuid());
            var lastOtherBox = await GetLastClosedAsync(cashierAOnB);
            Assert.Equal(otherTerminal.Id, lastOtherBox.GetProperty("id").GetGuid());

            using var opened = await cashierAOnA.PostAsJsonAsync("/api/cashier-shifts",
                new { openingCash = 40m });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            using var openedBody = JsonDocument.Parse(await opened.Content.ReadAsStringAsync());
            Assert.Equal(40m, openedBody.RootElement.GetProperty("openingCash").GetDecimal());
            Assert.Equal(40m, openedBody.RootElement.GetProperty("cashBalance").GetDecimal());
            Assert.Equal(previous.Id,
                (await GetLastClosedAsync(cashierAOnA)).GetProperty("id").GetGuid());
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.CashLedgerMovements.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.CashierShifts.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Users.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.PosTerminals.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Branches.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Roles.Where(item => item.Id == role.Id).ExecuteDeleteAsync();
                await db.Companies.Where(item => item.Id == company.Id).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            }
        }

        CashierShift ClosedShift(Guid cashierId, Guid terminalId, decimal openingCash, int hour)
        {
            var opened = new DateTimeOffset(2026, 11, 4, hour, 0, 0, TimeSpan.Zero);
            var shift = new CashierShift(company.Id, branch.Id, cashierId, openingCash,
                opened, terminalId);
            shift.Close(cashierId, opened.AddMinutes(30));
            return shift;
        }
    }

    private static async Task<HttpClient> LoginAsync(VisualApiFactory factory,
        Company company, Branch branch, UserIdentity user, string password,
        string terminalCredential)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", terminalCredential);
        using var login = await client.PostAsJsonAsync("/api/sessions",
            new { companyCode = company.Code, credential = user.Username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie",
            Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
        using var context = await client.PutAsJsonAsync("/api/sessions/current/context",
            new { companyId = company.Id, branchId = branch.Id });
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        return client;
    }

    private static async Task<JsonElement> GetLastClosedAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/cashier-shifts/last-closed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private sealed class VisualApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:PlatformAccess"] = connectionString }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PlatformAccessDbContext>();
                services.RemoveAll<DbContextOptions<PlatformAccessDbContext>>();
                services.AddDbContext<PlatformAccessDbContext>(options => options.UseNpgsql(connectionString));
            });
        }
    }
}

public sealed class I15ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I15ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I15_WRITES_APPROVED") != "I15_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
