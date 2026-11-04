using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Api.ExcelImport;
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
public sealed class CatalogCodeSearchVisualEndpointTests
{
    [I06ApprovedVisualWriteFact]
    [Trait("Category", "I06ApprovedDatabaseWrite")]
    public async Task MainAndActiveAlternateResolveSameArticleAcrossAdminAndImports()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I06 fixture {suffix}", $"I06-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I06");
        var role = new Role($"I06-{suffix}", "I06 administrator");
        var password = $"I06-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i06-{suffix}",
            $"i06-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var category = new ProductCategory(company.Id, "Prueba I06");
        var product = new CatalogProduct(company.Id, category.Id, $"P{suffix}",
            "Artículo I06", "u.", ProductSaleMode.Unit, 10m);
        var activeCode = new ProductCode(company.Id, product.Id, $"A{suffix}");
        var inactiveCode = new ProductCode(company.Id, product.Id, $"X{suffix}");
        inactiveCode.Deactivate();
        var list = new PriceList(company.Id, $"Lista I06 {suffix}");
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                var permissionIds = await db.Permissions.Where(item =>
                    item.Code == PlatformPermissionCatalog.CatalogManage ||
                    item.Code == PlatformPermissionCatalog.InventoryStockManage)
                    .Select(item => item.Id).ToArrayAsync();
                Assert.Equal(2, permissionIds.Length);
                db.AddRange(company, branch, role, user,
                    new UserAssignment(user.Id, role.Id, company.Id), category,
                    product, activeCode, inactiveCode, list,
                    new BranchPriceList(company.Id, branch.Id, list.Id),
                    new BranchInventoryBalance(company.Id, branch.Id, product.Id));
                db.RolePermissions.AddRange(permissionIds.Select(id => new RoleCapability(role.Id, id)));
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            var login = await client.PostAsJsonAsync("/api/sessions",
                new { companyCode = company.Code, credential = user.Username, password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            client.DefaultRequestHeaders.Add("Cookie",
                Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
            var context = await client.PutAsJsonAsync("/api/sessions/current/context",
                new { companyId = company.Id, branchId = branch.Id });
            Assert.True(context.StatusCode == HttpStatusCode.OK,
                await context.Content.ReadAsStringAsync());

            foreach (var code in new[] { product.Code, activeCode.Code })
            {
                var products = await GetAsync(client, $"/api/admin/products?search={code}");
                Assert.Equal(1, products.GetProperty("totalItems").GetInt64());
                Assert.Equal(product.Id, products.GetProperty("items")[0].GetProperty("id").GetGuid());
                var prices = await GetAsync(client,
                    $"/api/admin/price-lists/{list.Id}/prices?search={code}");
                Assert.Equal(1, prices.GetProperty("totalItems").GetInt64());
                Assert.Equal(product.Id, prices.GetProperty("items")[0]
                    .GetProperty("productId").GetGuid());
            }
            var inactiveProducts = await GetAsync(client,
                $"/api/admin/products?search={inactiveCode.Code}");
            Assert.Equal(0, inactiveProducts.GetProperty("totalItems").GetInt64());
            var inactivePrices = await GetAsync(client,
                $"/api/admin/price-lists/{list.Id}/prices?search={inactiveCode.Code}");
            Assert.Equal(0, inactivePrices.GetProperty("totalItems").GetInt64());
            var stock = await GetAsync(client, "/api/inventory/stock");
            var stockItem = Assert.Single(stock.EnumerateArray());
            Assert.Equal(product.Id, stockItem.GetProperty("productId").GetGuid());
            Assert.Equal(activeCode.Code, Assert.Single(stockItem.GetProperty("alternateCodes")
                .EnumerateArray()).GetString());

            await using (var db = new PlatformAccessDbContext(options))
            {
                var stockPreview = await AdminExcelImportProcessor.AssessAsync(db, company.Id,
                    branch.Id, user.Id, "stock", new ParsedImport([
                        new ImportRow("Existencias", 2, new Dictionary<string, ImportCell>
                        {
                            ["CodigoArticulo"] = new(activeCode.Code, null),
                            ["Variacion"] = new(null, 1m),
                            ["Descripcion"] = new(null, null)
                        })], []), DateTimeOffset.UtcNow, false, CancellationToken.None);
                Assert.True(stockPreview.CanApply);
                var inactivePreview = await AdminExcelImportProcessor.AssessAsync(db, company.Id,
                    branch.Id, user.Id, "stock", new ParsedImport([
                        new ImportRow("Existencias", 2, new Dictionary<string, ImportCell>
                        {
                            ["CodigoArticulo"] = new(inactiveCode.Code, null),
                            ["Variacion"] = new(null, 1m),
                            ["Descripcion"] = new(null, null)
                        })], []), DateTimeOffset.UtcNow, false, CancellationToken.None);
                Assert.Contains(inactivePreview.Issues, item => item.Code == "PRODUCT_NOT_AVAILABLE");
                var pricePreview = await AdminExcelImportProcessor.AssessAsync(db, company.Id,
                    branch.Id, user.Id, "price-lists", new ParsedImport([
                        new ImportRow("Precios", 2, new Dictionary<string, ImportCell>
                        {
                            ["Lista"] = new(list.Name, null),
                            ["CodigoArticulo"] = new(activeCode.Code, null),
                            ["PrecioARS"] = new(null, 25m)
                        })], []), DateTimeOffset.UtcNow, false, CancellationToken.None);
                Assert.True(pricePreview.CanApply);
                Assert.Contains(pricePreview.Rows, item => item.Status == "create");
                var inactivePricePreview = await AdminExcelImportProcessor.AssessAsync(db,
                    company.Id, branch.Id, user.Id, "price-lists", new ParsedImport([
                        new ImportRow("Precios", 2, new Dictionary<string, ImportCell>
                        {
                            ["Lista"] = new(list.Name, null),
                            ["CodigoArticulo"] = new(inactiveCode.Code, null),
                            ["PrecioARS"] = new(null, 25m)
                        })], []), DateTimeOffset.UtcNow, false, CancellationToken.None);
                Assert.Contains(inactivePricePreview.Issues,
                    item => item.Code == "PRODUCT_NOT_FOUND");
            }
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.UserId == user.Id).ExecuteDeleteAsync();
                await db.ProductCodes.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.BranchInventoryBalances.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.BranchPriceLists.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.CatalogProducts.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.ProductCategories.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.PriceLists.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.RolePermissions.Where(item => item.RoleId == role.Id).ExecuteDeleteAsync();
                await db.Users.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Branches.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Roles.Where(item => item.Id == role.Id).ExecuteDeleteAsync();
                await db.Companies.Where(item => item.Id == company.Id).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            }
        }
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
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

public sealed class I06ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I06ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I06_WRITES_APPROVED") != "I06_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
