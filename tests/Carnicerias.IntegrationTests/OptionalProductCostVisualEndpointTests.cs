using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Api.ExcelImport;
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
public sealed class OptionalProductCostVisualEndpointTests
{
    [I09ApprovedVisualWriteFact]
    [Trait("Category", "I09ApprovedDatabaseWrite")]
    public async Task UnknownCostAllowsImportAndSaleBeforeFirstCostVersion()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I09 fixture {suffix}", $"I09-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I09");
        var role = new Role($"I09-{suffix}", "I09 operator");
        var password = $"I09-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i09-{suffix}",
            $"i09-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var terminalCredential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, branch.Id, "Caja I09");
        terminal.AssignCredentialHash(terminalCredential.Hash);
        var category = new ProductCategory(company.Id, "Carnes I09");
        var list = new PriceList(company.Id, "Mostrador I09");
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var permissionId = await db.Permissions.Where(item =>
                    item.Code == PlatformPermissionCatalog.CatalogManage)
                    .Select(item => item.Id).SingleAsync();
                db.AddRange(company, branch, role, user,
                    new UserAssignment(user.Id, role.Id, company.Id),
                    new RoleCapability(role.Id, permissionId), terminal, category, list,
                    new BranchPriceList(company.Id, branch.Id, list.Id));
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", terminalCredential.Token);
            await LoginAsync(client, company, branch, user, password);

            var productCode = $"P{suffix}";
            var created = await OkAsync(await client.PostAsJsonAsync("/api/admin/products", new
            {
                categoryId = category.Id, code = productCode, name = "Asado I09",
                unit = "un", saleMode = "unit", cost = (decimal?)null
            }), HttpStatusCode.Created);
            var productId = created.GetProperty("id").GetGuid();
            Assert.Equal(JsonValueKind.Null, created.GetProperty("cost").ValueKind);
            var initialHistory = await OkAsync(await client.GetAsync(
                $"/api/admin/products/{productId}/cost-history"));
            Assert.Equal(0, initialHistory.GetArrayLength());

            var firstPrice = await OkAsync(await client.GetAsync(
                $"/api/admin/price-lists/{list.Id}/prices?search={productCode}"));
            Assert.Equal(JsonValueKind.Null,
                firstPrice.GetProperty("items")[0].GetProperty("cost").ValueKind);
            await OkAsync(await client.PutAsJsonAsync(
                $"/api/admin/price-lists/{list.Id}/products/{productId}/price",
                new { amount = 200m }));
            await using (var db = new PlatformAccessDbContext(options))
            {
                var balance = new BranchInventoryBalance(company.Id, branch.Id, productId);
                balance.SetQuantities(2m, 0m);
                db.BranchInventoryBalances.Add(balance);
                await db.SaveChangesAsync();
            }
            var available = await OkAsync(await client.GetAsync(
                $"/api/catalog/products?priceListId={list.Id}&code={productCode}"));
            Assert.Equal(productId,
                available.GetProperty("items")[0].GetProperty("id").GetGuid());

            await OkAsync(await client.PostAsJsonAsync("/api/cashier-shifts",
                new { openingCash = 0m }), HttpStatusCode.Created);
            var draft = await OkAsync(await client.PutAsJsonAsync("/api/sales/draft", new
            { priceListId = list.Id, lines = new[] { new { productId, quantity = 1m } } }));
            var draftId = draft.GetProperty("id").GetGuid();
            var sale = await OkAsync(await client.PostAsJsonAsync(
                $"/api/sales/drafts/{draftId}/confirmation",
                new { payments = new[] { new { method = "cash", amount = 200m } } }),
                HttpStatusCode.Created);
            Assert.Equal(200m, sale.GetProperty("total").GetDecimal());
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Single(await db.ConfirmedSales.Where(item => item.CompanyId == company.Id)
                    .ToArrayAsync());
                Assert.Equal(1m, (await db.BranchInventoryBalances.SingleAsync(item =>
                    item.CompanyId == company.Id)).OnHand);
                Assert.Null((await db.CatalogProducts.SingleAsync(item => item.Id == productId)).Cost);
                Assert.Empty(await db.ProductCostVersions.Where(item => item.ProductId == productId)
                    .ToArrayAsync());
            }

            var firstCost = await OkAsync(await client.PatchAsJsonAsync(
                $"/api/admin/products/{productId}", new { cost = 100.50m }));
            Assert.Equal(100.50m, firstCost.GetProperty("cost").GetDecimal());
            var history = await OkAsync(await client.GetAsync(
                $"/api/admin/products/{productId}/cost-history"));
            Assert.Equal(1, history.GetArrayLength());
            Assert.Equal(100.50m, history[0].GetProperty("amount").GetDecimal());
            Assert.Equal(JsonValueKind.Null, history[0].GetProperty("effectiveToUtc").ValueKind);
            var revised = await OkAsync(await client.PatchAsJsonAsync(
                $"/api/admin/products/{productId}", new { cost = 120.75m }));
            Assert.Equal(120.75m, revised.GetProperty("cost").GetDecimal());
            var revisedHistory = await OkAsync(await client.GetAsync(
                $"/api/admin/products/{productId}/cost-history"));
            Assert.Equal(2, revisedHistory.GetArrayLength());
            Assert.Equal(120.75m, revisedHistory[0].GetProperty("amount").GetDecimal());
            Assert.Equal(100.50m, revisedHistory[1].GetProperty("amount").GetDecimal());
            Assert.NotEqual(JsonValueKind.Null,
                revisedHistory[1].GetProperty("effectiveToUtc").ValueKind);

            var excelCode = $"X{suffix}";
            var parsed = new ParsedImport([
                new ImportRow("Articulos", 2, new Dictionary<string, ImportCell>
                {
                    ["Codigo"] = new(excelCode, null),
                    ["Nombre"] = new("Producto Excel I09", null),
                    ["Categoria"] = new(category.Name, null),
                    ["ModalidadVenta"] = new("unit", null),
                    ["Unidad"] = new("un", null),
                    ["CostoARS"] = new(null, null)
                })], []);
            await using (var db = new PlatformAccessDbContext(options))
            {
                var result = await AdminExcelImportProcessor.AssessAsync(db,
                    company.Id, branch.Id, user.Id, "products", parsed,
                    DateTimeOffset.UtcNow, true, CancellationToken.None);
                Assert.True(result.CanApply);
                Assert.Equal(1, result.CreateCount);
                await db.SaveChangesAsync();
                var imported = await db.CatalogProducts.SingleAsync(item =>
                    item.CompanyId == company.Id && item.Code == excelCode);
                Assert.Null(imported.Cost);
                Assert.Empty(await db.ProductCostVersions.Where(item =>
                    item.ProductId == imported.Id).ToArrayAsync());
            }
        }
        finally
        {
            if (seeded) await RemoveFixtureAsync(options, company.Id, role.Id);
        }
    }

    private static async Task LoginAsync(HttpClient client, Company company, Branch branch,
        UserIdentity user, string password)
    {
        using var login = await client.PostAsJsonAsync("/api/sessions",
            new { companyCode = company.Code, credential = user.Username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie",
            Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
        using var context = await client.PutAsJsonAsync("/api/sessions/current/context",
            new { companyId = company.Id, branchId = branch.Id });
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected,
                $"Expected {(int)expected}, received {(int)response.StatusCode}: {body}");
            if (string.IsNullOrWhiteSpace(body)) return default;
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }

    private static async Task RemoveFixtureAsync(DbContextOptions<PlatformAccessDbContext> options,
        Guid companyId, Guid roleId)
    {
        await using var db = new PlatformAccessDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var saleIds = db.ConfirmedSales.Where(item => item.CompanyId == companyId).Select(item => item.Id);
        var checkoutIds = db.SaleCheckouts.Where(item => item.CompanyId == companyId).Select(item => item.Id);
        await db.Sessions.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
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
        await db.ProductCostVersions.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.BranchPriceLists.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.CatalogProducts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.ProductCategories.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.PriceLists.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.UserAssignments.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.RolePermissions.Where(item => item.RoleId == roleId).ExecuteDeleteAsync();
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

public sealed class I09ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I09ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I09_WRITES_APPROVED") != "I09_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
