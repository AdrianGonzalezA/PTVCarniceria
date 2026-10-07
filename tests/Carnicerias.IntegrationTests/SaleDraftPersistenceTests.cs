using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
public sealed class SaleDraftPersistenceTests
{
    [PostgreSqlFact]
    public async Task CashierCanConfirmAnotherSaleAfterAnInsufficientStockDraftIsCorrected()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (databaseName?.StartsWith("carnicerias_test_", StringComparison.OrdinalIgnoreCase) != true ||
            databaseName.Equals("carnicerias_test_visual", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use an empty, disposable PostgreSQL test database other than the visual database.");

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>().UseNpgsql(connectionString).Options;
        const string password = "Sale Checkout Test Password 42!";
        try
        {
            Guid companyId;
            Guid branchId;
            Guid productId;
            Guid priceListId;
            await using (var db = new PlatformAccessDbContext(options))
            {
                await db.Database.MigrateAsync();
                var bootstrap = new BootstrapAdminService(db, new Argon2idPasswordHasher());
                await bootstrap.CreateFirstAdministratorAsync("sale-test-admin", "sale-test@example.test",
                    "Sale Test Company", "Sale Test Branch", password, PlatformPermissionCatalog.CreateDefaultPermissions());
                var company = await db.Companies.SingleAsync();
                var branch = await db.Branches.SingleAsync();
                var user = await db.Users.SingleAsync();
                var role = await db.Roles.SingleAsync(item => item.Code == "administrator");
                var otherCashier = UserIdentity.Create("sale-test-other", "sale-other@example.test",
                    new Argon2idPasswordHasher().Hash(password));
                db.AddRange(otherCashier, new UserAssignment(otherCashier.Id, role.Id, company.Id));
                companyId = company.Id;
                branchId = branch.Id;
                var category = new ProductCategory(companyId, "Meat");
                var product = new CatalogProduct(companyId, category.Id, "MEAT-1", "Test cut", "kg", ProductSaleMode.Weight, 500);
                var priceList = new PriceList(companyId, "Counter");
                productId = product.Id;
                priceListId = priceList.Id;
                var balance = new BranchInventoryBalance(companyId, branchId, productId);
                balance.SetQuantities(10, 0);
                db.AddRange(category, product, priceList,
                    new BranchPriceList(companyId, branchId, priceListId),
                    new ProductPrice(companyId, priceListId, productId, 1000, DateTimeOffset.UtcNow.AddMinutes(-1), user.Id),
                    balance);
                await db.SaveChangesAsync();
            }

            using var factory = new SaleApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var login = await client.PostAsJsonAsync("/api/sessions", new { credential = "sale-test-admin", password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie);

            using (var selectContext = new HttpRequestMessage(HttpMethod.Put, "/api/sessions/current/context")
            {
                Content = JsonContent.Create(new { companyId, branchId })
            })
            {
                selectContext.Headers.Add("Origin", "app://bundle");
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(selectContext)).StatusCode);
            }

            var firstSave = await client.PutAsJsonAsync("/api/sales/draft", new
            {
                priceListId,
                lines = new[] { new { productId, quantity = 1m } }
            });
            Assert.Equal(HttpStatusCode.OK, firstSave.StatusCode);
            using var firstBody = JsonDocument.Parse(await firstSave.Content.ReadAsStringAsync());
            var draftId = firstBody.RootElement.GetProperty("id").GetGuid();

            var updatedSave = await client.PutAsJsonAsync("/api/sales/draft", new
            {
                priceListId,
                lines = new[] { new { productId, quantity = 2m } }
            });
            Assert.Equal(HttpStatusCode.OK, updatedSave.StatusCode);
            using var updatedBody = JsonDocument.Parse(await updatedSave.Content.ReadAsStringAsync());
            Assert.Equal(draftId, updatedBody.RootElement.GetProperty("id").GetGuid());

            Assert.Equal(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/cashier-shifts", new { openingCash = 0m })).StatusCode);
            var confirmation = await client.PostAsJsonAsync($"/api/sales/drafts/{draftId}/confirmation", new
            {
                payments = new[] { new { method = "cash", amount = 2000m } }
            });
            Assert.Equal(HttpStatusCode.Created, confirmation.StatusCode);
            var retry = await client.PostAsJsonAsync($"/api/sales/drafts/{draftId}/confirmation", new
            {
                payments = new[] { new { method = "cash", amount = 2000m } }
            });
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

            using var otherClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            var otherLogin = await otherClient.PostAsJsonAsync("/api/sessions",
                new { credential = "sale-test-other", password });
            Assert.Equal(HttpStatusCode.OK, otherLogin.StatusCode);
            var otherCookie = Assert.Single(otherLogin.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
            otherClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", otherCookie);
            using (var otherContext = new HttpRequestMessage(HttpMethod.Put, "/api/sessions/current/context")
            {
                Content = JsonContent.Create(new { companyId, branchId })
            })
            {
                otherContext.Headers.Add("Origin", "app://bundle");
                Assert.Equal(HttpStatusCode.OK, (await otherClient.SendAsync(otherContext)).StatusCode);
            }
            var foreignRetry = await otherClient.PostAsJsonAsync(
                $"/api/sales/drafts/{draftId}/confirmation",
                new { payments = new[] { new { method = "cash", amount = 2000m } } });
            Assert.Equal(HttpStatusCode.Conflict, foreignRetry.StatusCode);

            var insufficientSecondDraft = await client.PutAsJsonAsync("/api/sales/draft", new
            {
                priceListId,
                lines = new[] { new { productId, quantity = 100m } }
            });
            Assert.Equal(HttpStatusCode.Conflict, insufficientSecondDraft.StatusCode);
            using (var errorBody = JsonDocument.Parse(await insufficientSecondDraft.Content.ReadAsStringAsync()))
                Assert.Equal("INSUFFICIENT_STOCK", errorBody.RootElement.GetProperty("error").GetProperty("code").GetString());
            var secondSave = await client.PutAsJsonAsync("/api/sales/draft", new
            {
                priceListId,
                lines = new[] { new { productId, quantity = 1m } }
            });
            Assert.Equal(HttpStatusCode.OK, secondSave.StatusCode);
            using var secondBody = JsonDocument.Parse(await secondSave.Content.ReadAsStringAsync());
            var secondDraftId = secondBody.RootElement.GetProperty("id").GetGuid();
            Assert.NotEqual(draftId, secondDraftId);
            Assert.Equal(HttpStatusCode.Created,
                (await client.PostAsJsonAsync($"/api/sales/drafts/{secondDraftId}/confirmation", new
                {
                    payments = new[] { new { method = "cash", amount = 1000m } }
                })).StatusCode);

            var currentShift = await client.GetAsync("/api/cashier-shifts/current");
            Assert.Equal(HttpStatusCode.OK, currentShift.StatusCode);
            using (var shiftBody = JsonDocument.Parse(await currentShift.Content.ReadAsStringAsync()))
            {
                Assert.Equal(3000m, shiftBody.RootElement.GetProperty("salesTotal").GetDecimal());
                Assert.Equal(3000m, shiftBody.RootElement.GetProperty("cashBalance").GetDecimal());
            }
            var closedShift = await client.PostAsync("/api/cashier-shifts/current/close", content: null);
            Assert.Equal(HttpStatusCode.OK, closedShift.StatusCode);
            using (var closedBody = JsonDocument.Parse(await closedShift.Content.ReadAsStringAsync()))
                Assert.Equal(3000m, closedBody.RootElement.GetProperty("cashBalance").GetDecimal());

            await using var verification = new PlatformAccessDbContext(options);
            var sale = await verification.ConfirmedSales.Include(item => item.Lines).Include(item => item.Payments)
                .SingleAsync(item => item.SourceDraftId == draftId);
            Assert.Equal(2000m, sale.Total);
            Assert.Single(sale.Lines);
            Assert.Single(sale.Payments);
            var savedDraft = await verification.SaleDrafts.SingleAsync(item => item.Id == draftId);
            Assert.Equal(SaleDraftStatus.Confirmed, savedDraft.Status);
            var secondSavedDraft = await verification.SaleDrafts.SingleAsync(item => item.Id == secondDraftId);
            Assert.Equal(SaleDraftStatus.Confirmed, secondSavedDraft.Status);
            Assert.Equal(2, await verification.ConfirmedSales.CountAsync());
            var stock = await verification.BranchInventoryBalances.SingleAsync(item => item.ProductId == productId);
            Assert.Equal(7m, stock.OnHand);
            Assert.Equal(0m, stock.Reserved);
            Assert.Equal(2, await verification.CashLedgerMovements.CountAsync());
        }
        finally
        {
            await using var cleanup = new PlatformAccessDbContext(options);
            await cleanup.Database.ExecuteSqlRawAsync("TRUNCATE TABLE pos_sales.sale_drafts CASCADE");
            await cleanup.Database.MigrateAsync("0");
        }
    }

    [PostgreSqlFact]
    public async Task UpdatingDraftKeepsExistingLineIdentityAndReconcilesAddedAndRemovedProducts()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (databaseName?.StartsWith("carnicerias_test_", StringComparison.OrdinalIgnoreCase) != true ||
            databaseName.Equals("carnicerias_test_visual", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use an empty, disposable PostgreSQL test database other than the visual database.");

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        try
        {
            await using var db = new PlatformAccessDbContext(options);
            await db.Database.MigrateAsync();

            var company = new Company("Draft Test Company");
            var branch = new Branch(company.Id, "Draft Test Branch");
            var user = UserIdentity.Create("draft-test-cashier", "draft-test@example.test", "test-only-hash");
            var category = new ProductCategory(company.Id, "Meat");
            var priceList = new PriceList(company.Id, "Counter");
            var firstProduct = new CatalogProduct(company.Id, category.Id, "A", "Product A", "kg", ProductSaleMode.Weight, 100);
            var removedProduct = new CatalogProduct(company.Id, category.Id, "B", "Product B", "kg", ProductSaleMode.Weight, 100);
            var addedProduct = new CatalogProduct(company.Id, category.Id, "C", "Product C", "kg", ProductSaleMode.Weight, 100);
            var now = DateTimeOffset.UtcNow;
            var draft = new SaleDraft(company.Id, branch.Id, user.Id, priceList.Id, now);
            draft.Lines.Add(new SaleDraftLine(company.Id, firstProduct.Id, "A", "Product A", "kg", ProductSaleMode.Weight, 1, 200));
            draft.Lines.Add(new SaleDraftLine(company.Id, removedProduct.Id, "B", "Product B", "kg", ProductSaleMode.Weight, 1, 200));
            db.AddRange(company, branch, user, category, priceList, firstProduct, removedProduct, addedProduct, draft);
            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            var loaded = await db.SaleDrafts.Include(item => item.Lines).SingleAsync(item => item.Id == draft.Id);
            var originalLineId = loaded.Lines.Single(item => item.ProductId == firstProduct.Id).Id;
            loaded.ReplaceLines([
                new SaleDraftLine(company.Id, firstProduct.Id, "A", "Product A", "kg", ProductSaleMode.Weight, 2, 200),
                new SaleDraftLine(company.Id, addedProduct.Id, "C", "Product C", "kg", ProductSaleMode.Weight, 1, 300)
            ], now.AddMinutes(1));
            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            var saved = await db.SaleDrafts.AsNoTracking().Include(item => item.Lines)
                .SingleAsync(item => item.Id == draft.Id);
            Assert.Equal(2, saved.Lines.Count);
            var updatedLine = Assert.Single(saved.Lines, item => item.ProductId == firstProduct.Id);
            Assert.Equal(originalLineId, updatedLine.Id);
            Assert.Equal(2, updatedLine.Quantity);
            Assert.DoesNotContain(saved.Lines, item => item.ProductId == removedProduct.Id);
            Assert.Contains(saved.Lines, item => item.ProductId == addedProduct.Id);
        }
        finally
        {
            await using var cleanup = new PlatformAccessDbContext(options);
            await cleanup.Database.MigrateAsync("0");
        }
    }

    private sealed class SaleApiFactory(string connectionString) : WebApplicationFactory<Program>
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
