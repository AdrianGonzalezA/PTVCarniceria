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
    public async Task TwoTerminalsKeepIndependentConcurrentShifts()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var databaseName = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database;
        Assert.StartsWith("carnicerias_test_", databaseName, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>().UseNpgsql(connectionString).Options;
        const string password = "Two Terminal Shift Password 42!";
        var firstCredential = PosTerminalCredential.Issue();
        var secondCredential = PosTerminalCredential.Issue();

        try
        {
            Guid companyId;
            Guid branchId;
            Guid productId;
            Guid priceListId;
            await using (var db = new PlatformAccessDbContext(options))
            {
                await db.Database.MigrateAsync();
                var hasher = new Argon2idPasswordHasher();
                await new BootstrapAdminService(db, hasher).CreateFirstAdministratorAsync(
                    "cashier-one", "cashier-one@example.test", "Shift Company", "Shift Branch",
                    password, PlatformPermissionCatalog.CreateDefaultPermissions());
                var company = await db.Companies.SingleAsync();
                var branch = await db.Branches.SingleAsync();
                var role = await db.Roles.SingleAsync();
                var secondUser = UserIdentity.Create("cashier-two", "cashier-two@example.test", hasher.Hash(password));
                var firstTerminal = new PosTerminal(company.Id, branch.Id, "Caja 1");
                var secondTerminal = new PosTerminal(company.Id, branch.Id, "Caja 2");
                firstTerminal.AssignCredentialHash(firstCredential.Hash);
                secondTerminal.AssignCredentialHash(secondCredential.Hash);
                var category = new ProductCategory(company.Id, "Meat");
                var product = new CatalogProduct(company.Id, category.Id, "MEAT-1", "Test cut",
                    "kg", ProductSaleMode.Weight, 500);
                var priceList = new PriceList(company.Id, "Counter");
                var stock = new BranchInventoryBalance(company.Id, branch.Id, product.Id);
                stock.SetQuantities(10m, 0m);
                db.AddRange(secondUser, new UserAssignment(secondUser.Id, role.Id, company.Id),
                    firstTerminal, secondTerminal, category, product, priceList,
                    new BranchPriceList(company.Id, branch.Id, priceList.Id),
                    new ProductPrice(company.Id, priceList.Id, product.Id, 1000m,
                        DateTimeOffset.UtcNow.AddMinutes(-1), (await db.Users.SingleAsync()).Id), stock);
                await db.SaveChangesAsync();
                companyId = company.Id;
                branchId = branch.Id;
                productId = product.Id;
                priceListId = priceList.Id;
            }

            using var factory = new CashierShiftApiFactory(connectionString);
            using var first = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            using var second = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            await LoginAndSelectTerminalAsync(first, "cashier-one", password, firstCredential.Token, companyId, branchId);
            await LoginAndSelectTerminalAsync(second, "cashier-two", password, secondCredential.Token, companyId, branchId);

            var opened = await Task.WhenAll(
                first.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 100m }),
                second.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 200m }));
            Assert.All(opened, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            using var firstBody = JsonDocument.Parse(await opened[0].Content.ReadAsStringAsync());
            using var secondBody = JsonDocument.Parse(await opened[1].Content.ReadAsStringAsync());
            var firstShiftId = firstBody.RootElement.GetProperty("id").GetGuid();
            var secondShiftId = secondBody.RootElement.GetProperty("id").GetGuid();
            Assert.NotEqual(firstShiftId, secondShiftId);

            using var firstCurrent = JsonDocument.Parse(await first.GetStringAsync("/api/cashier-shifts/current"));
            using var secondCurrent = JsonDocument.Parse(await second.GetStringAsync("/api/cashier-shifts/current"));
            Assert.Equal(firstShiftId, firstCurrent.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(secondShiftId, secondCurrent.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(100m, firstCurrent.RootElement.GetProperty("cashBalance").GetDecimal());
            Assert.Equal(200m, secondCurrent.RootElement.GetProperty("cashBalance").GetDecimal());

            Assert.Equal(HttpStatusCode.OK,
                (await first.PutAsJsonAsync("/api/sales/draft", new
                { priceListId, lines = new[] { new { productId, quantity = 2m } } })).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await second.PutAsJsonAsync("/api/sales/draft", new
                { priceListId, lines = new[] { new { productId, quantity = 3m } } })).StatusCode);
            await using (var stockDb = new PlatformAccessDbContext(options))
            {
                Assert.Equal(5m, (await stockDb.BranchInventoryBalances.SingleAsync()).Reserved);
                var drafts = await stockDb.SaleDrafts.ToArrayAsync();
                Assert.Equal(2, drafts.Length);
                Assert.Contains(drafts, draft => draft.CashierShiftId == firstShiftId);
                Assert.Contains(drafts, draft => draft.CashierShiftId == secondShiftId);
                Assert.NotEqual(drafts[0].PosTerminalId, drafts[1].PosTerminalId);
            }
            var pendingClose = await first.PostAsync("/api/cashier-shifts/current/close", null);
            Assert.Equal(HttpStatusCode.Conflict, pendingClose.StatusCode);
            using (var error = JsonDocument.Parse(await pendingClose.Content.ReadAsStringAsync()))
                Assert.Equal("CASHIER_SHIFT_HAS_DRAFT", error.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/cashier-shifts/current")).StatusCode);

            using var sameCashierOtherTerminal = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            await LoginAndSelectTerminalAsync(sameCashierOtherTerminal, "cashier-one", password,
                secondCredential.Token, companyId, branchId);
            Assert.Equal(HttpStatusCode.NoContent,
                (await sameCashierOtherTerminal.GetAsync("/api/sales/draft")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await sameCashierOtherTerminal.PutAsJsonAsync("/api/sales/draft", new
                { priceListId, lines = new[] { new { productId, quantity = 1m } } })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await sameCashierOtherTerminal.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 0m })).StatusCode);

            using var otherCashierSameTerminal = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            await LoginAndSelectTerminalAsync(otherCashierSameTerminal, "cashier-two", password,
                firstCredential.Token, companyId, branchId);
            Assert.Equal(HttpStatusCode.Conflict,
                (await otherCashierSameTerminal.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 0m })).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await first.DeleteAsync("/api/sales/draft")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/sales/draft")).StatusCode);
            await using (var stockDb = new PlatformAccessDbContext(options))
                Assert.Equal(3m, (await stockDb.BranchInventoryBalances.SingleAsync()).Reserved);

            var firstResaved = await first.PutAsJsonAsync("/api/sales/draft", new
            { priceListId, lines = new[] { new { productId, quantity = 2m } } });
            Assert.Equal(HttpStatusCode.OK, firstResaved.StatusCode);
            using var firstResavedBody = JsonDocument.Parse(await firstResaved.Content.ReadAsStringAsync());
            var firstDraftId = firstResavedBody.RootElement.GetProperty("id").GetGuid();
            using var secondDraftBody = JsonDocument.Parse(await second.GetStringAsync("/api/sales/draft"));
            var secondDraftId = secondDraftBody.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Created,
                (await first.PostAsJsonAsync($"/api/sales/drafts/{firstDraftId}/confirmation", new
                { payments = new[] { new { method = "cash", amount = 2000m } } })).StatusCode);
            Assert.Equal(HttpStatusCode.Created,
                (await second.PostAsJsonAsync($"/api/sales/drafts/{secondDraftId}/confirmation", new
                { payments = new[] { new { method = "cash", amount = 3000m } } })).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await first.PostAsJsonAsync($"/api/sales/drafts/{firstDraftId}/confirmation", new
                { payments = new[] { new { method = "cash", amount = 2000m } } })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await sameCashierOtherTerminal.PostAsJsonAsync($"/api/sales/drafts/{firstDraftId}/confirmation", new
                { payments = new[] { new { method = "cash", amount = 2000m } } })).StatusCode);
            await using (var stockDb = new PlatformAccessDbContext(options))
            {
                var stock = await stockDb.BranchInventoryBalances.SingleAsync();
                Assert.Equal(5m, stock.OnHand);
                Assert.Equal(0m, stock.Reserved);
                var sales = await stockDb.ConfirmedSales.ToArrayAsync();
                Assert.Equal(2, sales.Length);
                Assert.All(sales, sale => Assert.NotNull(sale.PosTerminalId));
                Assert.NotEqual(sales[0].PosTerminalId, sales[1].PosTerminalId);
                var ledger = await stockDb.CashLedgerMovements.ToArrayAsync();
                Assert.Equal(4, ledger.Length);
                Assert.All(ledger, movement => Assert.NotNull(movement.PosTerminalId));
            }

            Assert.Equal(HttpStatusCode.OK,
                (await second.PostAsync("/api/cashier-shifts/current/close", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await second.GetAsync("/api/cashier-shifts/current")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await sameCashierOtherTerminal.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 0m })).StatusCode);
            using var secondAgain = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            await LoginAndSelectTerminalAsync(secondAgain, "cashier-two", password,
                secondCredential.Token, companyId, branchId);
            Assert.Equal(HttpStatusCode.Created,
                (await secondAgain.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 200m })).StatusCode);

            Assert.Equal(HttpStatusCode.OK,
                (await first.PostAsync("/api/cashier-shifts/current/close", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/cashier-shifts/current")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await secondAgain.GetAsync("/api/cashier-shifts/current")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await secondAgain.GetAsync("/api/cashier-shifts/last-closed")).StatusCode);

            using var firstAgain = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            await LoginAndSelectTerminalAsync(firstAgain, "cashier-one", password,
                firstCredential.Token, companyId, branchId);

            using var firstClosed = JsonDocument.Parse(await firstAgain.GetStringAsync("/api/cashier-shifts/last-closed"));
            using var secondClosed = JsonDocument.Parse(await secondAgain.GetStringAsync("/api/cashier-shifts/last-closed"));
            Assert.Equal(2100m, firstClosed.RootElement.GetProperty("cashBalance").GetDecimal());
            Assert.Equal(3200m, secondClosed.RootElement.GetProperty("cashBalance").GetDecimal());

            await using var verifyDb = new PlatformAccessDbContext(options);
            var shifts = await verifyDb.CashierShifts.OrderBy(shift => shift.OpeningCash).ToArrayAsync();
            Assert.Equal(3, shifts.Length);
            Assert.NotNull(shifts[0].PosTerminalId);
            Assert.NotEqual(shifts[0].PosTerminalId, shifts[1].PosTerminalId);
            Assert.All(await verifyDb.CashLedgerMovements.ToArrayAsync(), movement =>
                Assert.NotNull(movement.PosTerminalId));
        }
        finally
        {
            await using var cleanup = new PlatformAccessDbContext(options);
            await cleanup.Database.ExecuteSqlRawAsync("TRUNCATE TABLE pos_sales.sale_drafts CASCADE");
            await cleanup.Database.MigrateAsync("0");
        }
    }

    private static async Task LoginAndSelectTerminalAsync(
        HttpClient client, string username, string password, string terminalCredential,
        Guid companyId, Guid branchId)
    {
        client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", terminalCredential);
        var login = await client.PostAsJsonAsync("/api/sessions", new { credential = username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/sessions/current/context", new { companyId, branchId })).StatusCode);
    }

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
