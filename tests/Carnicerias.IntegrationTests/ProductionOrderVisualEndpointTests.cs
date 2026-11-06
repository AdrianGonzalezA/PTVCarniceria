using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Domain.PlatformAccess;
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
public sealed class ProductionOrderVisualEndpointTests
{
    [I23ApprovedVisualWriteFact]
    [Trait("Category", "I23ApprovedDatabaseWrite")]
    public async Task CompetingOrdersReleaseAndClosePreservePieceAndStock()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I23 production fixture {suffix}", $"I23-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I23");
        var password = $"I23-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i23-{suffix}",
            $"i23-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var adminPassword = $"I22-{Guid.NewGuid():N}!";
        var admin = UserIdentity.CreateForCompany(company.Id, $"i22-admin-{suffix}",
            $"i22-admin-{suffix}@example.test",
            new Argon2idPasswordHasher().Hash(adminPassword));
        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, branch.Id, "Terminal I23");
        terminal.AssignCredentialHash(credential.Hash);
        var category = new ProductCategory(company.Id, "Producción I23");
        var inputProduct = new CatalogProduct(company.Id, category.Id, $"IN-{suffix}",
            "Insumo por peso", "kg", ProductSaleMode.Weight, null);
        var outputProduct = new CatalogProduct(company.Id, category.Id, $"OUT-{suffix}",
            "Producto por peso", "kg", ProductSaleMode.Weight, null);
        inputProduct.SetAnatomicalGroup("Lomo");
        var classifier = new ProductionClassifier(company.Id, "Desarme I23");
        var profile = new BarcodeProfile(company.Id, "Producción I23", 1,
            "pro_identif(6) peso(5)", "peso", 3, DateTime.UtcNow);
        var opening = new InventoryMovement(company.Id, branch.Id, inputProduct.Id,
            user.Id, Guid.NewGuid(), InventoryMovementKind.PieceReceipt, 3m,
            "Pieza de prueba I23", DateTimeOffset.UtcNow);
        var inputBalance = new BranchInventoryBalance(company.Id, branch.Id, inputProduct.Id);
        inputBalance.SetQuantities(3m, 0m, protectedOnHand: 3m);
        var piece = new InventoryPiece(company.Id, branch.Id, inputProduct.Id,
            profile.Id, user.Id, Guid.NewGuid(), opening.Id, "I23 fixture",
            "654321", "65432103000", 3m, DateTimeOffset.UtcNow);
        var seeded = false;
        try
        {
            Guid productionRoleId;
            Guid administratorRoleId;
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                productionRoleId = await db.Roles.Where(item => item.Code == "production")
                    .Select(item => item.Id).SingleAsync();
                administratorRoleId = await db.Roles.Where(item => item.Code == "administrator")
                    .Select(item => item.Id).SingleAsync();
                db.AddRange(company, branch, user, admin,
                    new UserAssignment(user.Id, productionRoleId, company.Id, branch.Id),
                    new UserAssignment(admin.Id, administratorRoleId, company.Id),
                    terminal, category, inputProduct, outputProduct, classifier, profile,
                    opening, inputBalance, piece);
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var first = await LogInAsync(factory, company, branch, user, password,
                credential.Token);
            using var second = await LogInAsync(factory, company, branch, user, password,
                credential.Token);
            using var catalogAdmin = await LogInAsync(factory, company, branch,
                admin, adminPassword, credential.Token);
            var groups = await OkAsync(await catalogAdmin.GetAsync(
                "/api/admin/products/anatomical-groups"));
            Assert.Contains(groups.EnumerateArray(), item => item.GetString() == "Lomo");
            var filtered = await OkAsync(await catalogAdmin.GetAsync(
                "/api/admin/products?anatomicalGroup=lomo"));
            Assert.Equal(1, filtered.GetProperty("totalItems").GetInt32());
            Assert.Equal(inputProduct.Id, filtered.GetProperty("items")[0]
                .GetProperty("id").GetGuid());
            var classified = await OkAsync(await catalogAdmin.PatchAsJsonAsync(
                $"/api/admin/products/{outputProduct.Id}",
                new { anatomicalGroup = "Lomo" }));
            Assert.Equal("Lomo", classified.GetProperty("anatomicalGroup").GetString());
            using (var forbidden = await first.GetAsync("/api/inventory/transfers"))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            var firstOrder = await CreateAsync(first, classifier.Id);
            var secondOrder = await CreateAsync(second, classifier.Id);
            var firstId = firstOrder.GetProperty("id").GetGuid();
            var secondId = secondOrder.GetProperty("id").GetGuid();
            var inputLine = new { productId = inputProduct.Id, direction = "Input",
                quantity = 3m, massKg = 3m, inputPieceId = (Guid?)piece.Id,
                outputBarcodeProfileId = (Guid?)null, outputSourceSystem = (string?)null,
                outputRawBarcode = (string?)null, outputIdentifierField = (string?)null };
            var attempts = await Task.WhenAll(
                first.PutAsJsonAsync($"/api/production/orders/{firstId}/lines",
                    new { version = 1, lines = new[] { inputLine } }),
                second.PutAsJsonAsync($"/api/production/orders/{secondId}/lines",
                    new { version = 1, lines = new[] { inputLine } }));
            var outcomes = await Task.WhenAll(attempts.Select(async item =>
                $"{(int)item.StatusCode}: {await item.Content.ReadAsStringAsync()}"));
            Assert.True(attempts.Count(item => item.StatusCode == HttpStatusCode.OK) == 1 &&
                attempts.Count(item => item.StatusCode == HttpStatusCode.Conflict) == 1,
                string.Join(" | ", outcomes));
            var winner = attempts[0].StatusCode == HttpStatusCode.OK ? first : second;
            var loser = ReferenceEquals(winner, first) ? second : first;
            var winnerId = ReferenceEquals(winner, first) ? firstId : secondId;
            var loserId = ReferenceEquals(winner, first) ? secondId : firstId;
            foreach (var attempt in attempts) attempt.Dispose();

            var recovered = await OkAsync(await winner.GetAsync(
                $"/api/production/orders/{winnerId}"));
            Assert.Equal("Open", recovered.GetProperty("status").GetString());
            Assert.Single(recovered.GetProperty("lines").EnumerateArray());
            using (var cancel = await winner.PostAsJsonAsync(
                $"/api/production/orders/{winnerId}/cancel", new { version = 2 }))
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            var resumed = await OkAsync(await loser.PutAsJsonAsync(
                $"/api/production/orders/{loserId}/lines",
                new { version = 1, lines = new[] { inputLine } }));
            Assert.Equal(2, resumed.GetProperty("version").GetInt32());

            var lines = new object[]
            {
                inputLine,
                new { productId = inputProduct.Id, direction = "Input",
                    quantity = 1m, massKg = 1m, inputPieceId = (Guid?)null,
                    outputBarcodeProfileId = (Guid?)null, outputSourceSystem = (string?)null,
                    outputRawBarcode = (string?)null, outputIdentifierField = (string?)null },
                new { productId = outputProduct.Id, direction = "Output",
                    quantity = 2m, massKg = 2m, inputPieceId = (Guid?)null,
                    outputBarcodeProfileId = (Guid?)profile.Id,
                    outputSourceSystem = "I23 fixture", outputRawBarcode = "12345602000",
                    outputIdentifierField = "pro_identif" },
                new { productId = outputProduct.Id, direction = "Output",
                    quantity = 1m, massKg = 1m, inputPieceId = (Guid?)null,
                    outputBarcodeProfileId = (Guid?)null, outputSourceSystem = (string?)null,
                    outputRawBarcode = (string?)null, outputIdentifierField = (string?)null }
            };
            var edited = await OkAsync(await loser.PutAsJsonAsync(
                $"/api/production/orders/{loserId}/lines", new { version = 2, lines }));
            Assert.Equal(3, edited.GetProperty("version").GetInt32());
            var closed = await OkAsync(await loser.PostAsJsonAsync(
                $"/api/production/orders/{loserId}/close",
                new { version = 3, reportedWasteKg = 1m, wasteReason = "Prueba I23" }));
            Assert.Equal("Closed", closed.GetProperty("status").GetString());
            Assert.Equal(4m, closed.GetProperty("inputMassKg").GetDecimal());
            Assert.Equal(3m, closed.GetProperty("outputMassKg").GetDecimal());
            Assert.Equal(0m, closed.GetProperty("massDifferenceKg").GetDecimal());
            var retry = await OkAsync(await loser.PostAsJsonAsync(
                $"/api/production/orders/{loserId}/close",
                new { version = 3, reportedWasteKg = 1m, wasteReason = "Prueba I23" }));
            Assert.Equal(closed.GetProperty("version").GetInt32(),
                retry.GetProperty("version").GetInt32());
            var summary = await OkAsync(await loser.GetAsync(
                "/api/production/orders/summary"));
            var row = Assert.Single(summary.EnumerateArray());
            Assert.Equal(classifier.Id, row.GetProperty("classifierId").GetGuid());
            Assert.Equal(1, row.GetProperty("orderCount").GetInt32());
            Assert.Equal(4m, row.GetProperty("inputMassKg").GetDecimal());
            Assert.Equal(3m, row.GetProperty("outputMassKg").GetDecimal());
            Assert.Equal(1m, row.GetProperty("reportedWasteKg").GetDecimal());

            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Equal(InventoryPieceStatus.Consumed,
                    (await db.InventoryPieces.AsNoTracking().SingleAsync(item => item.Id == piece.Id)).Status);
                var produced = await db.InventoryPieces.AsNoTracking().SingleAsync(item =>
                    item.CompanyId == company.Id && item.ProductionOrderId == loserId);
                Assert.Equal(InventoryPieceStatus.Available, produced.Status);
                Assert.Equal(2m, produced.ReceivedWeightKg);
                Assert.Equal("123456", produced.ExternalIdentifier);
                var sourceBalance = await db.BranchInventoryBalances.AsNoTracking()
                    .SingleAsync(item => item.CompanyId == company.Id &&
                        item.ProductId == inputProduct.Id);
                Assert.Equal(-1m, sourceBalance.OnHand);
                Assert.Equal(0m, sourceBalance.ProtectedOnHand);
                Assert.Equal(0m, sourceBalance.Reserved);
                var resultBalance = await db.BranchInventoryBalances.AsNoTracking()
                    .SingleAsync(item => item.CompanyId == company.Id &&
                        item.ProductId == outputProduct.Id);
                Assert.Equal(3m, resultBalance.OnHand);
                Assert.Equal(2m, resultBalance.ProtectedOnHand);
                Assert.Equal(4, await db.InventoryMovements.CountAsync(item =>
                    item.CompanyId == company.Id &&
                    (item.Kind == InventoryMovementKind.ProductionInput ||
                     item.Kind == InventoryMovementKind.ProductionOutput)));
                Assert.Equal(1, await db.InventoryPieceStateEvents.CountAsync(item =>
                    item.CompanyId == company.Id &&
                    item.Kind == InventoryPieceEventKind.ProductionConsumed));
            }
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.InventoryPieceStateEvents.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.ProductionMaterialLines.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryPieces.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryMovements.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.ProductionOrders.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.BranchInventoryBalances.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.PosTerminals.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.BarcodeProfiles.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.CatalogProducts.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.ProductCategories.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.ProductionClassifiers.Where(item => item.CompanyId == company.Id)
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

    private static async Task<HttpClient> LogInAsync(VisualApiFactory factory,
        Company company, Branch branch, UserIdentity user, string password,
        string terminalToken)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", terminalToken);
        using (var login = await client.PostAsJsonAsync("/api/sessions",
            new { companyCode = company.Code, credential = user.Username, password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            client.DefaultRequestHeaders.Add("Cookie",
                Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
        }
        using (var context = await client.PutAsJsonAsync("/api/sessions/current/context",
            new { companyId = company.Id, branchId = branch.Id }))
            Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        return client;
    }

    private static Task<JsonElement> CreateAsync(HttpClient client, Guid classifierId) =>
        OkAsync(client.PostAsJsonAsync("/api/production/orders",
            new { operationId = Guid.NewGuid(), classifierId,
                correctsOrderId = (Guid?)null }), HttpStatusCode.Created);

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> response,
        HttpStatusCode expected = HttpStatusCode.OK) =>
        await OkAsync(await response, expected);

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

public sealed class I23ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I23ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I23_WRITES_APPROVED") != "I23_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
