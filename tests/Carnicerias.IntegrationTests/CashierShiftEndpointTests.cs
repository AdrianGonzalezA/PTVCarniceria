using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Api.Sessions;
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

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class CashierShiftEndpointTests
{
    [PostgreSqlFact]
    public async Task CashierCanOpenCloseAndChangeBranchOnlyAfterClosingShift()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        if (connection.Database?.StartsWith("carnicerias_test_", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("The integration database name must start with 'carnicerias_test_'.");

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>().UseNpgsql(connectionString).Options;
        const string password = "Cashier Shift Test Password 42!";

        try
        {
            Guid companyId;
            Guid branchId;
            Guid otherBranchId;
            await using (var db = new PlatformAccessDbContext(options))
            {
                await db.Database.MigrateAsync();
                var bootstrap = new BootstrapAdminService(db, new Argon2idPasswordHasher());
                await bootstrap.CreateFirstAdministratorAsync("cashier-shift-admin", "cashier-shift@example.test",
                    "Shift Company", "Shift Branch", password, PlatformPermissionCatalog.CreateDefaultPermissions());
                var company = await db.Companies.SingleAsync();
                var branch = await db.Branches.SingleAsync();
                var otherBranch = new Branch(company.Id, "Other Branch");
                db.Branches.Add(otherBranch);
                await db.SaveChangesAsync();
                companyId = company.Id;
                branchId = branch.Id;
                otherBranchId = otherBranch.Id;
            }

            using var factory = new CashierShiftApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var login = await client.PostAsJsonAsync("/api/sessions", new { credential = "cashier-shift-admin", password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie);

            using (var selectRequest = new HttpRequestMessage(HttpMethod.Put, "/api/sessions/current/context")
            {
                Content = JsonContent.Create(new { companyId, branchId })
            })
            {
                selectRequest.Headers.Add("Origin", "app://bundle");
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(selectRequest)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/cashier-shifts/current")).StatusCode);
            var opened = await client.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 100m });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            using var openedBody = JsonDocument.Parse(await opened.Content.ReadAsStringAsync());
            var shiftId = openedBody.RootElement.GetProperty("id").GetGuid();
            await using (var ledgerDb = new PlatformAccessDbContext(options))
            {
                var cashierId = await ledgerDb.Users.Select(user => user.Id).SingleAsync();
                var now = DateTimeOffset.UtcNow;
                ledgerDb.CashLedgerMovements.AddRange(
                    new CashLedgerMovement(companyId, branchId, shiftId, cashierId, Guid.NewGuid(),
                        PaymentMethod.Cash, CashLedgerMovementKind.SalePayment, 1200m, now),
                    new CashLedgerMovement(companyId, branchId, shiftId, cashierId, Guid.NewGuid(),
                        PaymentMethod.Cash, CashLedgerMovementKind.Change, -200m, now),
                    new CashLedgerMovement(companyId, branchId, shiftId, cashierId, Guid.NewGuid(),
                        PaymentMethod.Debit, CashLedgerMovementKind.SalePayment, 500m, now));
                await ledgerDb.SaveChangesAsync();
            }
            var current = await client.GetAsync("/api/cashier-shifts/current");
            using (var currentBody = JsonDocument.Parse(await current.Content.ReadAsStringAsync()))
            {
                Assert.Equal(1100m, currentBody.RootElement.GetProperty("cashBalance").GetDecimal());
                Assert.Equal(1500m, currentBody.RootElement.GetProperty("salesTotal").GetDecimal());
            }
            Assert.Equal(HttpStatusCode.Conflict,
                (await client.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 100m })).StatusCode);

            using (var changeRequest = new HttpRequestMessage(HttpMethod.Put, "/api/sessions/current/context")
            {
                Content = JsonContent.Create(new { companyId, branchId = otherBranchId })
            })
            {
                changeRequest.Headers.Add("Origin", "app://bundle");
                Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(changeRequest)).StatusCode);
            }

            var closed = await client.PostAsync("/api/cashier-shifts/current/close", content: null);
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            using (var closedBody = JsonDocument.Parse(await closed.Content.ReadAsStringAsync()))
            {
                Assert.Equal(100m, closedBody.RootElement.GetProperty("openingCash").GetDecimal());
                Assert.Equal(1000m, closedBody.RootElement.GetProperty("cashSales").GetDecimal());
                Assert.Equal(500m, closedBody.RootElement.GetProperty("nonCashSales").GetDecimal());
                Assert.Equal(1100m, closedBody.RootElement.GetProperty("cashBalance").GetDecimal());
                Assert.Equal(1500m, closedBody.RootElement.GetProperty("salesTotal").GetDecimal());
            }
            var lastClosed = await client.GetAsync("/api/cashier-shifts/last-closed");
            Assert.Equal(HttpStatusCode.OK, lastClosed.StatusCode);
            using (var lastClosedBody = JsonDocument.Parse(await lastClosed.Content.ReadAsStringAsync()))
                Assert.Equal(1100m, lastClosedBody.RootElement.GetProperty("cashBalance").GetDecimal());

            using (var changeRequest = new HttpRequestMessage(HttpMethod.Put, "/api/sessions/current/context")
            {
                Content = JsonContent.Create(new { companyId, branchId = otherBranchId })
            })
            {
                changeRequest.Headers.Add("Origin", "app://bundle");
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(changeRequest)).StatusCode);
            }
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.GetAsync("/api/cashier-shifts/last-closed")).StatusCode);
        }
        finally
        {
            await using var cleanup = new PlatformAccessDbContext(options);
            await cleanup.Database.MigrateAsync("0");
        }
    }

    private sealed class CashierShiftApiFactory(string connectionString) : WebApplicationFactory<Program>
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
