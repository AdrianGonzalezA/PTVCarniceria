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
public sealed class SaleCheckoutVisualEndpointTests
{
    [I03AApprovedVisualWriteFact]
    [Trait("Category", "I03AApprovedDatabaseWrite")]
    public async Task PartialRepeatedPaymentsRecoverAndConfirmOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I03A HTTP fixture {suffix}", $"I03A-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I03A");
        var role = new Role($"I03A-{suffix}", "I03A cashier");
        var password = $"I03A-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i03a-{suffix}",
            $"i03a-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, branch.Id, "Caja I03A");
        terminal.AssignCredentialHash(credential.Hash);
        var category = new ProductCategory(company.Id, "Prueba I03A");
        var product = new CatalogProduct(company.Id, category.Id, $"I03A-{suffix}",
            "Producto de prueba", "unidad", ProductSaleMode.Unit, 50m);
        var list = new PriceList(company.Id, "Lista I03A");
        var balance = new BranchInventoryBalance(company.Id, branch.Id, product.Id);
        balance.SetQuantities(10m, 0m);
        var seeded = false;

        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                db.AddRange(company, branch, role, user,
                    new UserAssignment(user.Id, role.Id, company.Id), terminal,
                    category, product, list,
                    new BranchPriceList(company.Id, branch.Id, list.Id),
                    new ProductPrice(company.Id, list.Id, product.Id, 100m,
                        DateTimeOffset.UtcNow.AddMinutes(-1), user.Id), balance);
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var cashier = CreateClient(factory);
            await LoginAsync(cashier, user.Username, password, credential.Token,
                company.Id, branch.Id);
            await OkAsync(await cashier.PostAsJsonAsync("/api/cashier-shifts",
                new { openingCash = 0m }), HttpStatusCode.Created);
            var draftBody = await OkAsync(await cashier.PutAsJsonAsync("/api/sales/draft",
                new { priceListId = list.Id, lines = new[] { new { productId = product.Id, quantity = 2m } } }));
            var draftId = draftBody.GetProperty("id").GetGuid();
            var firstPayment = Guid.NewGuid();
            var secondPayment = Guid.NewGuid();
            var checkoutPath = $"/api/sales/drafts/{draftId}/checkout";
            var prepared = await OkAsync(await cashier.PutAsJsonAsync(checkoutPath, new
            {
                expectedRevision = 0,
                terms = new SaleCheckoutTerms(null, 0m, 0m, false,
                    "nonFiscalTicket", "finalConsumer"),
                payments = new[]
                {
                    new { id = firstPayment, method = "transfer", amount = 80m, reference = "I03A-1" },
                    new { id = secondPayment, method = "transfer", amount = 120m, reference = "I03A-2" }
                }
            }));
            var checkoutId = prepared.GetProperty("id").GetGuid();
            Assert.Equal(2, prepared.GetProperty("instruments").GetArrayLength());
            await using (var db = new PlatformAccessDbContext(options))
            {
                var draft = await db.SaleDrafts.Include(item => item.Lines)
                    .SingleAsync(item => item.Id == draftId);
                var checkout = await db.SaleCheckouts.SingleAsync(item => item.Id == checkoutId);
                Assert.Equal(draft.TotalAfterDiscount, checkout.Total);
                Assert.Equal(draft.UpdatedAtUtc, checkout.DraftUpdatedAtUtc);
            }
            var commitPath = $"{checkoutPath}/{checkoutId}/commit";
            var commitKey = Guid.NewGuid();
            var committed = await OkAsync(await cashier.PostAsJsonAsync(commitPath,
                new { expectedRevision = 1, key = commitKey }));
            Assert.Equal("Committed", committed.GetProperty("status").GetString());
            var replay = await OkAsync(await cashier.PostAsJsonAsync(commitPath,
                new { expectedRevision = 1, key = commitKey }));
            Assert.Equal(checkoutId, replay.GetProperty("id").GetGuid());

            var approvalPath = $"{checkoutPath}/{checkoutId}/instruments/{firstPayment}/approve";
            var partial = await OkAsync(await cashier.PostAsJsonAsync(approvalPath,
                new { reference = "I03A-1-approved" }));
            Assert.Equal("PendingBalance", partial.GetProperty("status").GetString());
            Assert.Equal(80m, partial.GetProperty("approvedAmount").GetDecimal());
            Assert.Equal(120m, partial.GetProperty("remainingAmount").GetDecimal());
            Assert.Equal(HttpStatusCode.Conflict,
                (await cashier.PostAsync($"{checkoutPath}/{checkoutId}/confirmation", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await cashier.PostAsync("/api/cashier-shifts/current/close", null)).StatusCode);
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.ConfirmedSales.Where(item => item.CompanyId == company.Id).ToArrayAsync());
                Assert.Empty(await db.CashLedgerMovements.Where(item => item.CompanyId == company.Id &&
                    item.Kind == CashLedgerMovementKind.SalePayment).ToArrayAsync());
                var stock = await db.BranchInventoryBalances.SingleAsync(item => item.CompanyId == company.Id);
                Assert.Equal(10m, stock.OnHand);
                Assert.Equal(2m, stock.Reserved);
            }

            using var recoveredCashier = CreateClient(factory);
            await LoginAsync(recoveredCashier, user.Username, password, credential.Token,
                company.Id, branch.Id);
            var recovered = await OkAsync(await recoveredCashier.GetAsync(checkoutPath));
            Assert.Equal(80m, recovered.GetProperty("approvedAmount").GetDecimal());
            Assert.Equal(checkoutId, recovered.GetProperty("id").GetGuid());
            var ready = await OkAsync(await recoveredCashier.PostAsJsonAsync(
                $"{checkoutPath}/{checkoutId}/instruments/{secondPayment}/approve",
                new { reference = "I03A-2-approved" }));
            Assert.Equal("Ready", ready.GetProperty("status").GetString());
            var confirmationPath = $"{checkoutPath}/{checkoutId}/confirmation";
            var sale = await OkAsync(await recoveredCashier.PostAsync(confirmationPath, null),
                HttpStatusCode.Created);
            var saleId = sale.GetProperty("id").GetGuid();
            var sameSale = await OkAsync(await recoveredCashier.PostAsync(confirmationPath, null));
            Assert.Equal(saleId, sameSale.GetProperty("id").GetGuid());
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Single(await db.ConfirmedSales.Where(item => item.CompanyId == company.Id).ToArrayAsync());
                var payments = await db.SalePayments.Where(item => item.SaleId == saleId).ToArrayAsync();
                Assert.Equal(2, payments.Length);
                Assert.All(payments, item => Assert.Equal(PaymentMethod.Transfer, item.Method));
                Assert.Equal(new[] { firstPayment, secondPayment }.Order(),
                    payments.Select(item => item.CheckoutInstrumentId!.Value).Order());
                var ledger = await db.CashLedgerMovements.Where(item =>
                    item.CompanyId == company.Id && item.Kind == CashLedgerMovementKind.SalePayment).ToArrayAsync();
                Assert.Equal(2, ledger.Length);
                Assert.Equal(200m, ledger.Sum(item => item.AmountDelta));
                var stock = await db.BranchInventoryBalances.SingleAsync(item => item.CompanyId == company.Id);
                Assert.Equal(8m, stock.OnHand);
                Assert.Equal(0m, stock.Reserved);
            }
        }
        finally
        {
            if (seeded) await RemoveFixtureAsync(options, company.Id, role.Id, user.Id);
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    private static async Task LoginAsync(HttpClient client, string username, string password,
        string terminalCredential, Guid companyId, Guid branchId)
    {
        client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", terminalCredential);
        var login = await client.PostAsJsonAsync("/api/sessions", new { credential = username, password });
        await OkAsync(login);
        client.DefaultRequestHeaders.Add("Cookie", Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
        await OkAsync(await client.PutAsJsonAsync("/api/sessions/current/context", new { companyId, branchId }));
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}, received {(int)response.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task RemoveFixtureAsync(
        DbContextOptions<PlatformAccessDbContext> options, Guid companyId, Guid roleId, Guid userId)
    {
        await using var db = new PlatformAccessDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var checkoutIds = db.SaleCheckouts.Where(item => item.CompanyId == companyId).Select(item => item.Id);
        var saleIds = db.ConfirmedSales.Where(item => item.CompanyId == companyId).Select(item => item.Id);
        await db.Sessions.Where(item => item.UserId == userId).ExecuteDeleteAsync();
        await db.SalePayments.Where(item => saleIds.Contains(item.SaleId)).ExecuteDeleteAsync();
        await db.SaleCheckoutEvents.Where(item => checkoutIds.Contains(item.SaleCheckoutId)).ExecuteDeleteAsync();
        await db.SaleCheckoutInstruments.Where(item => checkoutIds.Contains(item.SaleCheckoutId)).ExecuteDeleteAsync();
        await db.SaleCheckouts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.FiscalDocuments.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.CashLedgerMovements.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.ConfirmedSaleLines.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.ConfirmedSales.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.InventoryMovements.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.Set<SaleDraftLine>().Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.SaleDrafts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.CashierShifts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.BranchInventoryBalances.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.ProductPrices.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.BranchPriceLists.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.CatalogProducts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.ProductCategories.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.PriceLists.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.UserAssignments.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.PosTerminals.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.Users.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.Branches.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.Roles.Where(item => item.Id == roleId).ExecuteDeleteAsync();
        await db.Companies.Where(item => item.Id == companyId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
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

public sealed class I03AApprovedVisualWriteFactAttribute : FactAttribute
{
    public I03AApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I03A_WRITES_APPROVED") != "I03A_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Requires authorization for isolated I03A writes in the single visual database.";
    }
}
