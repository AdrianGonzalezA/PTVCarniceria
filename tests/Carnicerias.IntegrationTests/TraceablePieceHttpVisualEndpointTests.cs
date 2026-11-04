using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Domain.Inventory;
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
public sealed class TraceablePieceHttpVisualEndpointTests
{
    [I02HttpApprovedVisualWriteFact]
    [Trait("Category", "I02HttpApprovedDatabaseWrite")]
    public async Task TwoCashiersRecoverReleaseAndSellOnePieceOnlyOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I02 HTTP fixture {suffix}", $"I02HTTP-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I02 HTTP");
        var role = new Role($"I02HTTP-{suffix}", "I02 HTTP cashier");
        var password = $"I02HTTP-{Guid.NewGuid():N}!";
        var firstUser = UserIdentity.CreateForCompany(company.Id, $"i02httpa-{suffix}",
            $"i02httpa-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var secondUser = UserIdentity.CreateForCompany(company.Id, $"i02httpb-{suffix}",
            $"i02httpb-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var firstCredential = PosTerminalCredential.Issue();
        var secondCredential = PosTerminalCredential.Issue();
        var firstTerminal = new PosTerminal(company.Id, branch.Id, "Caja I02 A");
        var secondTerminal = new PosTerminal(company.Id, branch.Id, "Caja I02 B");
        firstTerminal.AssignCredentialHash(firstCredential.Hash);
        secondTerminal.AssignCredentialHash(secondCredential.Hash);
        var category = new ProductCategory(company.Id, "Trazables I02");
        var product = new CatalogProduct(company.Id, category.Id, $"I02-{suffix}",
            "Pieza HTTP", "kg", ProductSaleMode.Weight, 50m);
        var list = new PriceList(company.Id, "Mostrador I02");
        var profile = new BarcodeProfile(company.Id, "I02 HTTP", 1,
            "pro_identif(6) peso(5)", "peso", 3, DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var movement = new InventoryMovement(company.Id, branch.Id, product.Id,
            firstUser.Id, Guid.NewGuid(), InventoryMovementKind.PieceReceipt, 1.2m,
            "Fixture I02 HTTP", now);
        var barcode = $"I02HTTP{suffix}";
        var piece = new InventoryPiece(company.Id, branch.Id, product.Id, profile.Id,
            firstUser.Id, Guid.NewGuid(), movement.Id, "I02 HTTP fixture", suffix,
            barcode, 1.2m, now);
        var balance = new BranchInventoryBalance(company.Id, branch.Id, product.Id);
        balance.SetQuantities(1.2m, 0m);
        var seeded = false;

        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                db.AddRange(company, branch, role, firstUser, secondUser,
                    new UserAssignment(firstUser.Id, role.Id, company.Id),
                    new UserAssignment(secondUser.Id, role.Id, company.Id),
                    firstTerminal, secondTerminal, category, product, list,
                    new BranchPriceList(company.Id, branch.Id, list.Id),
                    new ProductPrice(company.Id, list.Id, product.Id, 100m,
                        now.AddMinutes(-1), firstUser.Id), profile, movement, piece, balance);
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var boxA = CreateClient(factory);
            using var boxB = CreateClient(factory);
            await LoginAsync(boxA, company, branch, firstUser, password, firstCredential.Token);
            await LoginAsync(boxB, company, branch, secondUser, password, secondCredential.Token);
            await OkAsync(await boxA.PostAsJsonAsync("/api/cashier-shifts",
                new { openingCash = 0m }), HttpStatusCode.Created);
            await OkAsync(await boxB.PostAsJsonAsync("/api/cashier-shifts",
                new { openingCash = 0m }), HttpStatusCode.Created);

            var lookup = await OkAsync(await boxA.GetAsync(
                $"/api/pos/pieces/lookup?code={barcode}"));
            Assert.Equal(piece.Id, lookup.GetProperty("id").GetGuid());
            Assert.Equal(1.2m, lookup.GetProperty("receivedWeightKg").GetDecimal());
            var firstDraft = await OkAsync(await boxA.PutAsJsonAsync("/api/sales/drafts/A",
                new { priceListId = list.Id, lines = new[]
                    { new { productId = product.Id, quantity = 1.2m, inventoryPieceId = piece.Id } } }));
            var firstDraftId = firstDraft.GetProperty("id").GetGuid();
            await AssertConflictAsync(await boxB.PutAsJsonAsync("/api/sales/drafts/A",
                new { priceListId = list.Id, lines = new[]
                    { new { productId = product.Id, quantity = 1.2m, inventoryPieceId = piece.Id } } }),
                "PIECE_ALREADY_USED");
            await AssertConflictAsync(await boxB.GetAsync(
                $"/api/pos/pieces/lookup?code={barcode}"), "PIECE_RESERVED");

            // A new HTTP session on the same physical terminal recovers the draft and its lease.
            using (var recoveredA = CreateClient(factory))
            {
                await LoginAsync(recoveredA, company, branch, firstUser, password,
                    firstCredential.Token);
                var recovered = await OkAsync(await recoveredA.GetAsync("/api/sales/drafts/A"));
                Assert.Equal(firstDraftId, recovered.GetProperty("id").GetGuid());
                Assert.Equal(piece.Id, recovered.GetProperty("lines")[0]
                    .GetProperty("inventoryPieceId").GetGuid());
                using var cancellation = await recoveredA.DeleteAsync("/api/sales/drafts/A");
                Assert.Equal(HttpStatusCode.NoContent, cancellation.StatusCode);
            }
            var released = await OkAsync(await boxB.GetAsync(
                $"/api/pos/pieces/lookup?code={barcode}"));
            Assert.Equal(piece.Id, released.GetProperty("id").GetGuid());

            var secondDraft = await OkAsync(await boxB.PutAsJsonAsync("/api/sales/drafts/A",
                new { priceListId = list.Id, lines = new[]
                    { new { productId = product.Id, quantity = 1.2m, inventoryPieceId = piece.Id } } }));
            var secondDraftId = secondDraft.GetProperty("id").GetGuid();
            var sale = await OkAsync(await boxB.PostAsJsonAsync(
                $"/api/sales/drafts/{secondDraftId}/confirmation",
                new { payments = new[] { new { method = "cash", amount = 120m } } }),
                HttpStatusCode.Created);
            var saleId = sale.GetProperty("id").GetGuid();
            Assert.Equal(120m, sale.GetProperty("total").GetDecimal());
            var replay = await OkAsync(await boxB.PostAsJsonAsync(
                $"/api/sales/drafts/{secondDraftId}/confirmation",
                new { payments = new[] { new { method = "cash", amount = 120m } } }));
            Assert.Equal(saleId, replay.GetProperty("id").GetGuid());
            await AssertConflictAsync(await boxA.GetAsync(
                $"/api/pos/pieces/lookup?code={barcode}"), "PIECE_ALREADY_SOLD");
            await AssertConflictAsync(await boxA.PutAsJsonAsync("/api/sales/drafts/B",
                new { priceListId = list.Id, lines = new[]
                    { new { productId = product.Id, quantity = 1.2m, inventoryPieceId = piece.Id } } }),
                "PIECE_ALREADY_USED");

            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Equal(InventoryPieceStatus.Sold,
                    (await db.InventoryPieces.SingleAsync(item => item.Id == piece.Id)).Status);
                Assert.Single(await db.ConfirmedSales.Where(item => item.CompanyId == company.Id)
                    .ToArrayAsync());
                Assert.Single(await db.ConfirmedSaleLines.Where(item =>
                    item.InventoryPieceId == piece.Id).ToArrayAsync());
                Assert.Equal(2, await db.InventoryPieceStateEvents.CountAsync(item =>
                    item.PieceId == piece.Id && item.Kind == InventoryPieceEventKind.SaleReserved));
                Assert.Equal(1, await db.InventoryPieceStateEvents.CountAsync(item =>
                    item.PieceId == piece.Id && item.Kind == InventoryPieceEventKind.SaleReleased));
                Assert.Equal(1, await db.InventoryPieceStateEvents.CountAsync(item =>
                    item.PieceId == piece.Id && item.Kind == InventoryPieceEventKind.SaleConfirmed));
                var currentBalance = await db.BranchInventoryBalances.SingleAsync(item =>
                    item.CompanyId == company.Id);
                Assert.Equal(0m, currentBalance.OnHand);
                Assert.Equal(0m, currentBalance.Reserved);
                Assert.Equal(0m, currentBalance.Quarantined);
                Assert.Equal(0m, currentBalance.Available);
            }
        }
        finally
        {
            if (seeded) await RemoveFixtureAsync(options, company.Id, role.Id);
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    private static async Task LoginAsync(HttpClient client, Company company, Branch branch,
        UserIdentity user, string password, string terminalCredential)
    {
        client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", terminalCredential);
        using var login = await client.PostAsJsonAsync("/api/sessions",
            new { companyCode = company.Code, credential = user.Username, password });
        await OkAsync(login);
        client.DefaultRequestHeaders.Add("Cookie",
            Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
        await OkAsync(await client.PutAsJsonAsync("/api/sessions/current/context",
            new { companyId = company.Id, branchId = branch.Id }));
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

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        var body = await OkAsync(response, HttpStatusCode.Conflict);
        Assert.Equal(code, body.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task RemoveFixtureAsync(
        DbContextOptions<PlatformAccessDbContext> options, Guid companyId, Guid roleId)
    {
        await using var db = new PlatformAccessDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var saleIds = db.ConfirmedSales.Where(item => item.CompanyId == companyId)
            .Select(item => item.Id);
        await db.Sessions.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.SalePayments.Where(item => saleIds.Contains(item.SaleId)).ExecuteDeleteAsync();
        await db.CashLedgerMovements.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.ConfirmedSaleLines.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.ConfirmedSales.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.InventoryPieceStateEvents.Where(item => item.CompanyId == companyId)
            .ExecuteDeleteAsync();
        await db.Set<SaleDraftLine>().Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.InventoryPieces.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.SaleDrafts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.InventoryMovements.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.CashierShifts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.BranchInventoryBalances.Where(item => item.CompanyId == companyId)
            .ExecuteDeleteAsync();
        await db.ProductPrices.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.BranchPriceLists.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.BarcodeProfiles.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
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
                services.AddDbContext<PlatformAccessDbContext>(options =>
                    options.UseNpgsql(connectionString));
            });
        }
    }
}

public sealed class I02HttpApprovedVisualWriteFactAttribute : FactAttribute
{
    public I02HttpApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I02_HTTP_WRITES_APPROVED") !=
            "I02_HTTP_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
