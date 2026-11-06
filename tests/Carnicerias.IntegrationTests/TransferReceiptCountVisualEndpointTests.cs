using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Api.Security;
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
public sealed class TransferReceiptCountVisualEndpointTests
{
    [I18BI21ApprovedVisualWriteFact]
    [Trait("Category", "I18BI21ApprovedDatabaseWrite")]
    public async Task DestinationReceiptAndCountAreIdempotentAndAStaleCountRequiresRecount()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I18b I21 fixture {suffix}", $"I21-{suffix}");
        var branch = new Branch(company.Id, "Destino de prueba");
        var category = new ProductCategory(company.Id, "Artículos de prueba");
        var product = new CatalogProduct(company.Id, category.Id, $"U{suffix}",
            "Artículo unitario de prueba", "un", ProductSaleMode.Unit, null);
        var password = $"I21-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i21-{suffix}",
            $"i21-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        Guid roleId;
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                roleId = await db.Roles.Where(item => item.Code == "administrator")
                    .Select(item => item.Id).SingleAsync();
                var receiptPermissionId = await db.Permissions.Where(item =>
                        item.Code == PlatformPermissionCatalog.InventoryReceiptsManage)
                    .Select(item => item.Id).SingleAsync();
                db.AddRange(company, branch, category, product, user,
                    new UserAssignment(user.Id, roleId, company.Id),
                    new CompanyRolePermissionOverride(company.Id, roleId,
                        receiptPermissionId, true, user.Id, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            using (var login = await client.PostAsJsonAsync("/api/sessions",
                new { companyCode = company.Code, credential = user.Username, password }))
            {
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                client.DefaultRequestHeaders.Add("Cookie",
                    Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
            }
            using (var context = await client.PutAsJsonAsync(
                "/api/sessions/current/context", new { companyId = company.Id,
                    branchId = branch.Id }))
                Assert.Equal(HttpStatusCode.OK, context.StatusCode);

            var receipt = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/transfer-receipts", new {
                    operationId = Guid.NewGuid(), sourceBranchName = "Origen externo",
                    documentNumber = $"RM-{suffix}",
                    documentDate = new DateOnly(2026, 11, 6) }), HttpStatusCode.Created);
            var receiptId = receipt.GetProperty("id").GetGuid();
            var lineAdded = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/transfer-receipts/{receiptId}/lines", new {
                    version = receipt.GetProperty("version").GetInt32(),
                    productId = product.Id, sentQuantity = 3m,
                    receivedQuantity = 2m, isPiece = false }));
            var approvedReceipt = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/transfer-receipts/{receiptId}/approve", new {
                    version = lineAdded.GetProperty("version").GetInt32() }));
            Assert.Equal("Approved", approvedReceipt.GetProperty("status").GetString());
            Assert.Equal("PendingReview", approvedReceipt.GetProperty("lines")[0]
                .GetProperty("differenceStatus").GetString());
            await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/transfer-receipts/{receiptId}/approve", new { version = 1 }));

            using (var policy = await client.PatchAsJsonAsync(
                $"/api/inventory/count-policy/categories/{category.Id}",
                new { policy = "MassObservation" }))
                Assert.Equal(HttpStatusCode.OK, policy.StatusCode);

            var count = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/counts", new { operationId = Guid.NewGuid() }),
                HttpStatusCode.Created);
            var countId = count.GetProperty("id").GetGuid();
            var counted = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/counts/{countId}/lines", new { version = 1,
                    productId = product.Id, countedQuantity = 1m }));
            Assert.Equal(2m, counted.GetProperty("lines")[0]
                .GetProperty("systemOnHand").GetDecimal());
            Assert.Equal("TheoreticalStock", counted.GetProperty("lines")[0]
                .GetProperty("policy").GetString());
            var submitted = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/counts/{countId}/submit", new {
                    version = counted.GetProperty("version").GetInt32() }));
            var approvedCount = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/counts/{countId}/approve", new {
                    version = submitted.GetProperty("version").GetInt32(),
                    reason = "Conteo verificado" }));
            Assert.Equal("Approved", approvedCount.GetProperty("status").GetString());
            await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/counts/{countId}/approve", new {
                    version = 1, reason = "Conteo verificado" }));

            var stale = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/counts", new { operationId = Guid.NewGuid() }),
                HttpStatusCode.Created);
            var staleId = stale.GetProperty("id").GetGuid();
            var staleLine = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/counts/{staleId}/lines", new { version = 1,
                    productId = product.Id, countedQuantity = 1m }));
            using (var adjustment = await client.PostAsJsonAsync(
                "/api/inventory/adjustments", new { operationId = Guid.NewGuid(),
                    productId = product.Id, quantityDelta = 1m,
                    reason = "Cambio durante el conteo" }))
                Assert.Equal(HttpStatusCode.OK, adjustment.StatusCode);
            var staleSubmitted = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/counts/{staleId}/submit", new {
                    version = staleLine.GetProperty("version").GetInt32() }));
            using (var conflict = await client.PostAsJsonAsync(
                $"/api/inventory/counts/{staleId}/approve", new {
                    version = staleSubmitted.GetProperty("version").GetInt32(),
                    reason = "No debe aprobar" }))
            {
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
                Assert.Contains("COUNT_STALE_RECOUNT_REQUIRED",
                    await conflict.Content.ReadAsStringAsync());
            }
            await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/counts/{staleId}/reject", new {
                    version = staleSubmitted.GetProperty("version").GetInt32(),
                    reason = "Recuento necesario" }));

            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Equal(2m, (await db.BranchInventoryBalances.SingleAsync(item =>
                    item.CompanyId == company.Id && item.BranchId == branch.Id &&
                    item.ProductId == product.Id)).OnHand);
                Assert.Single(await db.InventoryMovements.Where(item =>
                    item.CompanyId == company.Id &&
                    item.Kind == InventoryMovementKind.TransferIn).ToArrayAsync());
                Assert.Single(await db.InventoryMovements.Where(item =>
                    item.CompanyId == company.Id &&
                    item.Kind == InventoryMovementKind.PhysicalCountAdjustment).ToArrayAsync());
            }
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.PhysicalStockCountLines.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.PhysicalStockCounts.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryTransferReceiptLines.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryTransferReceipts.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryMovements.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.BranchInventoryBalances.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.CompanyRolePermissionOverrides.Where(item =>
                    item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.CatalogProducts.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.ProductCategories.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Users.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Branches.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Companies.Where(item => item.Id == company.Id)
                    .ExecuteDeleteAsync();
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
                services.AddDbContext<PlatformAccessDbContext>(options =>
                    options.UseNpgsql(connectionString));
            });
        }
    }
}

public sealed class I18BI21ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I18BI21ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I18B_I21_WRITES_APPROVED") !=
                "I18B_I21_APPROVED" || string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
