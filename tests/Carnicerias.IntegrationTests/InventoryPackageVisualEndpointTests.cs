using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Api.ExcelImport;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using ClosedXML.Excel;
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
public sealed class InventoryPackageVisualEndpointTests
{
    [I20ApprovedVisualWriteFact]
    [Trait("Category", "I20ApprovedDatabaseWrite")]
    public async Task TwoSixPacksOneTwelvePackAndTwoLooseUnitsEnterStockOnce()
    {
        var connection = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I20 package fixture {suffix}", $"I20-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I20");
        var role = new Role($"I20-{suffix}", "Receptor I20");
        var password = $"I20-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i20-{suffix}",
            $"i20-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, branch.Id, "Terminal I20");
        terminal.AssignCredentialHash(credential.Hash);
        var category = new ProductCategory(company.Id, "Mercadería I20");
        var product = new CatalogProduct(company.Id, category.Id, $"P{suffix}",
            "Chorizo I20", "u.", ProductSaleMode.Unit, null);
        var six = new ProductCode(company.Id, product.Id, $"S{suffix}",
            ProductCodeKind.PurchasePackage, 6);
        var twelve = new ProductCode(company.Id, product.Id, $"T{suffix}",
            ProductCodeKind.PurchasePackage, 12);
        var supplier = new Supplier(company.Id, $"F{suffix}", "Proveedor I20");
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var permission = await db.Permissions.SingleAsync(item =>
                    item.Code == PlatformPermissionCatalog.InventoryReceiptsManage);
                db.AddRange(company, branch, role, user,
                    new UserAssignment(user.Id, role.Id, company.Id, branch.Id),
                    new RoleCapability(role.Id, permission.Id), terminal, category,
                    product, six, twelve, supplier);
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connection);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", credential.Token);
            var login = await client.PostAsJsonAsync("/api/sessions",
                new { companyCode = company.Code, credential = user.Username, password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            client.DefaultRequestHeaders.Add("Cookie",
                Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
            var context = await client.PutAsJsonAsync("/api/sessions/current/context",
                new { companyId = company.Id, branchId = branch.Id });
            Assert.Equal(HttpStatusCode.OK, context.StatusCode);

            var created = await OkAsync(await client.PostAsJsonAsync("/api/inventory/receipts",
                new { operationId = Guid.NewGuid(), supplierId = supplier.Id,
                    documentType = "remito", documentNumber = "I20-1",
                    documentDate = new DateOnly(2026, 11, 6) }), HttpStatusCode.Created);
            var receiptId = created.GetProperty("id").GetGuid();
            var firstOperation = Guid.NewGuid();
            var first = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/packages",
                new { version = 1, operationId = firstOperation, code = six.Code,
                    packageCount = 2 }));
            Assert.Equal(12, first.GetProperty("packageReads")[0].GetProperty("unitQuantity").GetInt32());
            var retried = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/packages",
                new { version = 1, operationId = firstOperation, code = six.Code,
                    packageCount = 2 }));
            Assert.Equal(first.GetProperty("version").GetInt32(),
                retried.GetProperty("version").GetInt32());
            var second = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/packages",
                new { version = 2, operationId = Guid.NewGuid(), code = twelve.Code,
                    packageCount = 1 }));
            Assert.Equal(2, second.GetProperty("packageReads").GetArrayLength());
            var edited = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/lines",
                new { version = 3, lines = new[] {
                    new { productId = product.Id, receivedQuantity = 2m,
                        expectedQuantity = (decimal?)null }
                } }));
            Assert.Equal(3, edited.GetProperty("lines").GetArrayLength());
            Assert.Equal(2, edited.GetProperty("packageReads").GetArrayLength());
            var approved = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/approve", new { version = 4 }));
            Assert.Equal("Approved", approved.GetProperty("status").GetString());
            var again = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/approve", new { version = 4 }));
            Assert.Equal(approved.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());

            var importedDraft = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/receipts", new { operationId = Guid.NewGuid(),
                    supplierId = supplier.Id, documentType = "remito",
                    documentNumber = "I20-Excel", documentDate = new DateOnly(2026, 11, 6) }),
                HttpStatusCode.Created);
            var importedId = importedDraft.GetProperty("id").GetGuid();
            using var workbook = new XLWorkbook(new MemoryStream(
                AdminExcelWorkbook.CreateTemplate("receipts")));
            workbook.Worksheet("Bultos").Cell(2, 1).Value = six.Code;
            workbook.Worksheet("Bultos").Cell(2, 2).Value = 1;
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var bytes = stream.ToArray();
            var preview = await OkAsync(await client.PostAsync(
                $"/api/inventory/receipts/{importedId}/import/preview?version=1",
                SpreadsheetContent(bytes)));
            Assert.True(preview.GetProperty("canApply").GetBoolean());
            var imported = await OkAsync(await client.PostAsync(
                $"/api/inventory/receipts/{importedId}/import/apply?version=1",
                SpreadsheetContent(bytes)));
            Assert.Equal(6, imported.GetProperty("packageReads")[0]
                .GetProperty("unitQuantity").GetInt32());
            await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{importedId}/cancel",
                new { version = imported.GetProperty("version").GetInt32() }));

            await using var verification = new PlatformAccessDbContext(options);
            var balance = await verification.BranchInventoryBalances.SingleAsync(item =>
                item.CompanyId == company.Id && item.ProductId == product.Id);
            Assert.Equal(26, balance.OnHand);
            Assert.Equal(26, await verification.InventoryMovements.Where(item =>
                item.CompanyId == company.Id).SumAsync(item => item.QuantityDelta));
            Assert.Equal(3, await verification.InventoryReceiptPackageReads.CountAsync(item =>
                item.CompanyId == company.Id));
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.InventoryReceiptPackageReads.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryReceiptLines.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryReceipts.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryMovements.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.BranchInventoryBalances.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.ProductCodes.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Suppliers.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.RolePermissions.Where(item => item.RoleId == role.Id)
                    .ExecuteDeleteAsync();
                await db.PosTerminals.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.CatalogProducts.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.ProductCategories.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Users.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Branches.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Roles.Where(item => item.Id == role.Id).ExecuteDeleteAsync();
                await db.Companies.Where(item => item.Id == company.Id).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            }
        }
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected,
                $"Expected {(int)expected}, received {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }

    private static ByteArrayContent SpreadsheetContent(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return content;
    }

    private sealed class VisualApiFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:PlatformAccess"] = connection }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PlatformAccessDbContext>();
                services.RemoveAll<DbContextOptions<PlatformAccessDbContext>>();
                services.AddDbContext<PlatformAccessDbContext>(options => options.UseNpgsql(connection));
            });
        }
    }
}

public sealed class I20ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I20ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I20_WRITES_APPROVED") != "I20_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
