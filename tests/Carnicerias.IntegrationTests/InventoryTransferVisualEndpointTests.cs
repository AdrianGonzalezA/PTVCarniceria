using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carnicerias.Infrastructure;
using Carnicerias.Api.History;
using Carnicerias.Api.Security;
using Carnicerias.Domain.PlatformAccess;
using Carnicerias.PlatformAccess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
public sealed class InventoryTransferVisualEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [I18BTransferApprovedVisualWriteFact]
    [Trait("Category", "I18BApprovedDatabaseWrite")]
    public async Task DispatchIsIndependentOfDestinationAndConcurrentPieceDispatchCannotDuplicateStock()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I18b transfer fixture {suffix}", $"I18BT-{suffix}");
        var origin = new Branch(company.Id, "Origen I18b");
        var destination = new Branch(company.Id, "Destino I18b");
        var role = new Role($"I18BT-{suffix}", "Despachante I18b");
        var password = $"I18BT-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i18bt-{suffix}",
            $"i18bt-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, origin.Id, "Terminal I18b");
        terminal.AssignCredentialHash(credential.Hash);
        var category = new ProductCategory(company.Id, "Mercadería I18b");
        var unit = new CatalogProduct(company.Id, category.Id, $"U{suffix}",
            "Artículo por unidad", "un", ProductSaleMode.Unit, null);
        var weight = new CatalogProduct(company.Id, category.Id, $"W{suffix}",
            "Pieza trazable", "kg", ProductSaleMode.Weight, null);
        var profile = new BarcodeProfile(company.Id, "I18b HTTP", 1,
            "pro_identif(6) peso(5)", "peso", 3, DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var unitBalance = new BranchInventoryBalance(company.Id, origin.Id, unit.Id);
        unitBalance.SetQuantities(5, 0);
        var weightBalance = new BranchInventoryBalance(company.Id, origin.Id, weight.Id);
        // A prior generic sale may leave a deficit without consuming the protected piece.
        weightBalance.SetQuantities(1.500m, 0, protectedOnHand: 2.500m,
            allowGenericDeficit: true);
        var initialMovement = new InventoryMovement(company.Id, origin.Id, weight.Id,
            user.Id, Guid.NewGuid(), InventoryMovementKind.PieceReceipt, 2.500m,
            "Pieza aislada I18b", now);
        var unitOpening = new InventoryMovement(company.Id, origin.Id, unit.Id,
            user.Id, Guid.NewGuid(), InventoryMovementKind.OpeningBalance, 5m,
            "Saldo inicial I18b", now);
        var genericDeficit = new InventoryMovement(company.Id, origin.Id, weight.Id,
            user.Id, Guid.NewGuid(), InventoryMovementKind.Adjustment, -1m,
            "Déficit genérico previo I18b", now.AddTicks(1));
        var piece = new InventoryPiece(company.Id, origin.Id, weight.Id, profile.Id,
            user.Id, Guid.NewGuid(), initialMovement.Id, "I18b fixture",
            $"T{suffix}", $"T{suffix}02500", 2.500m, now);
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var permissionId = await db.Permissions.Where(item =>
                    item.Code == PlatformPermissionCatalog.InventoryTransfersManage)
                    .Select(item => item.Id).SingleAsync();
                db.AddRange(company, origin, destination, role, user,
                    new UserAssignment(user.Id, role.Id, company.Id, origin.Id),
                    new RoleCapability(role.Id, permissionId), terminal, category,
                    unit, weight, profile, unitBalance, weightBalance,
                    initialMovement, unitOpening, genericDeficit, piece);
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", credential.Token);
            using (var login = await client.PostAsJsonAsync("/api/sessions",
                new { companyCode = company.Code, credential = user.Username, password }))
            {
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                client.DefaultRequestHeaders.Add("Cookie",
                    Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
            }
            using (var context = await client.PutAsJsonAsync("/api/sessions/current/context",
                new { companyId = company.Id, branchId = origin.Id }))
                Assert.Equal(HttpStatusCode.OK, context.StatusCode);

            var branchOptions = await OkAsync(await client.GetAsync(
                "/api/inventory/transfers/branch-options"));
            Assert.Contains(branchOptions.EnumerateArray(), item =>
                item.GetProperty("id").GetGuid() == destination.Id);
            using (var wrongDestination = await client.PostAsJsonAsync(
                "/api/inventory/transfers", new { operationId = Guid.NewGuid(),
                    destinationName = "Otro lugar", destinationBranchId = destination.Id,
                    documentDate = new DateOnly(2026, 11, 5) }))
                Assert.Equal(HttpStatusCode.BadRequest, wrongDestination.StatusCode);
            var pieceOptions = await OkAsync(await client.GetAsync(
                $"/api/inventory/transfers/piece-options?productId={weight.Id}"));
            Assert.Equal(piece.Id, pieceOptions[0].GetProperty("id").GetGuid());

            var documentDate = new DateOnly(2026, 11, 5);
            var canceled = await CreateAsync(client, "Destino sin sistema", null, documentDate);
            using (var cancel = await client.PostAsJsonAsync(
                $"/api/inventory/transfers/{canceled.GetProperty("id").GetGuid()}/cancel",
                new { version = 1 }))
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

            var generic = await CreateAsync(client, "Destino sin sistema", null, documentDate);
            var genericId = generic.GetProperty("id").GetGuid();
            var genericEdited = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/transfers/{genericId}/lines",
                new { version = 1, lines = new[] {
                    new { productId = unit.Id, quantity = 2m, pieceId = (Guid?)null }
                } }));
            Assert.Equal(2, genericEdited.GetProperty("version").GetInt32());
            var genericCorrected = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/transfers/{genericId}/lines",
                new { version = 2, lines = new[] {
                    new { productId = unit.Id, quantity = 2m, pieceId = (Guid?)null }
                } }));
            Assert.Equal(3, genericCorrected.GetProperty("version").GetInt32());
            Assert.Single(genericCorrected.GetProperty("lines").EnumerateArray());
            using (var prematureExport = await client.GetAsync(
                $"/api/inventory/transfers/{genericId}/export"))
                Assert.Equal(HttpStatusCode.Conflict, prematureExport.StatusCode);
            var genericDispatch = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/transfers/{genericId}/dispatch", new { version = 3 }));
            Assert.Equal("Dispatched", genericDispatch.GetProperty("status").GetString());
            Assert.Null(genericDispatch.GetProperty("destinationBranchId").GetString());
            var retry = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/transfers/{genericId}/dispatch", new { version = 3 }));
            Assert.Equal(genericDispatch.GetProperty("version").GetInt32(),
                retry.GetProperty("version").GetInt32());
            using (var export = await client.GetAsync(
                $"/api/inventory/transfers/{genericId}/export"))
            {
                Assert.Equal(HttpStatusCode.OK, export.StatusCode);
                Assert.Equal("application/json", export.Content.Headers.ContentType?.MediaType);
                Assert.Contains(genericDispatch.GetProperty("documentNumber").GetString()!,
                    export.Content.Headers.ContentDisposition?.FileName ?? string.Empty);
                using var exported = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
                Assert.Equal(1, exported.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal(genericId, exported.RootElement.GetProperty("transferId").GetGuid());
                Assert.Equal("Destino sin sistema",
                    exported.RootElement.GetProperty("destinationName").GetString());
                Assert.Equal(2m, exported.RootElement.GetProperty("lines")[0]
                    .GetProperty("quantity").GetDecimal());
            }

            var insufficient = await CreateAsync(client, "Destino sin sistema", null, documentDate);
            var insufficientId = insufficient.GetProperty("id").GetGuid();
            var insufficientEdited = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/transfers/{insufficientId}/lines",
                new { version = 1, lines = new[] {
                    new { productId = unit.Id, quantity = 4m, pieceId = (Guid?)null }
                } }));
            using (var blocked = await client.PostAsJsonAsync(
                $"/api/inventory/transfers/{insufficientId}/dispatch",
                new { version = insufficientEdited.GetProperty("version").GetInt32() }))
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

            var first = await CreateAsync(client, "Destino I18b", destination.Id, documentDate);
            var second = await CreateAsync(client, "Destino I18b", destination.Id, documentDate);
            var firstId = first.GetProperty("id").GetGuid();
            var secondId = second.GetProperty("id").GetGuid();
            using (var wrongPieceWeight = await client.PutAsJsonAsync(
                $"/api/inventory/transfers/{firstId}/lines",
                new { version = 1, lines = new[] {
                    new { productId = weight.Id, quantity = 2.400m,
                        pieceId = (Guid?)piece.Id }
                } }))
                Assert.Equal(HttpStatusCode.Conflict, wrongPieceWeight.StatusCode);
            var firstEdited = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/transfers/{firstId}/lines",
                new { version = 1, lines = new[] {
                    new { productId = weight.Id, quantity = 2.500m, pieceId = (Guid?)piece.Id }
                } }));
            Assert.Equal(piece.ExternalIdentifier, firstEdited.GetProperty("lines")[0]
                .GetProperty("pieceIdentifier").GetString());
            var secondEdited = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/transfers/{secondId}/lines",
                new { version = 1, lines = new[] {
                    new { productId = weight.Id, quantity = 2.500m, pieceId = (Guid?)piece.Id }
                } }));
            var attempts = await Task.WhenAll(
                client.PostAsJsonAsync($"/api/inventory/transfers/{firstId}/dispatch",
                    new { version = firstEdited.GetProperty("version").GetInt32() }),
                client.PostAsJsonAsync($"/api/inventory/transfers/{secondId}/dispatch",
                    new { version = secondEdited.GetProperty("version").GetInt32() }));
            Assert.Equal(1, attempts.Count(item => item.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, attempts.Count(item => item.StatusCode == HttpStatusCode.Conflict));
            foreach (var attempt in attempts) attempt.Dispose();
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Equal(3, (await db.BranchInventoryBalances.AsNoTracking()
                    .SingleAsync(item => item.CompanyId == company.Id &&
                        item.BranchId == origin.Id && item.ProductId == unit.Id)).OnHand);
                var remaining = await db.BranchInventoryBalances.AsNoTracking()
                    .SingleAsync(item => item.CompanyId == company.Id &&
                        item.BranchId == origin.Id && item.ProductId == weight.Id);
                Assert.Equal(-1.000m, remaining.OnHand);
                Assert.Equal(0, remaining.ProtectedOnHand);
                Assert.Equal(InventoryPieceStatus.Transferred,
                    (await db.InventoryPieces.AsNoTracking().SingleAsync(item => item.Id == piece.Id)).Status);
                Assert.Equal(2, await db.InventoryMovements.CountAsync(item =>
                    item.CompanyId == company.Id && item.Kind == InventoryMovementKind.TransferOut));
                Assert.False(await db.BranchInventoryBalances.AnyAsync(item =>
                    item.CompanyId == company.Id && item.BranchId == destination.Id));
                Assert.Equal(1, await db.InventoryPieceStateEvents.CountAsync(item =>
                    item.CompanyId == company.Id && item.Kind == InventoryPieceEventKind.Transferred));
                var accessor = new OperationalContextAccessor();
                accessor.Set(new OperationalContext(user.Id, company.Id, origin.Id,
                    new HashSet<string> { PlatformPermissionCatalog.OrganizationManage },
                    Guid.NewGuid()), null);
                var kardex = await AdminHistoryEndpoints.ListStockMovementsAsync(db, accessor,
                    CancellationToken.None, branchId: origin.Id, kind: "transferOut");
                var body = JsonSerializer.SerializeToElement(
                    Assert.IsAssignableFrom<IValueHttpResult>(kardex).Value, JsonOptions);
                Assert.Equal(2, body.GetProperty("totalItems").GetInt32());
                Assert.Contains(body.GetProperty("items").EnumerateArray(), item =>
                    item.GetProperty("documentId").GetGuid() == genericId &&
                    item.GetProperty("documentKind").GetString() == "transfer" &&
                    item.GetProperty("documentNumber").GetString() ==
                        genericDispatch.GetProperty("documentNumber").GetString() &&
                    item.GetProperty("runningBalance").GetDecimal() == 3m);
            }
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.InventoryTransferLines.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryTransfers.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryPieceStateEvents.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryPieces.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryMovements.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.BranchInventoryBalances.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.RolePermissions.Where(item => item.RoleId == role.Id)
                    .ExecuteDeleteAsync();
                await db.PosTerminals.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.BarcodeProfiles.Where(item => item.CompanyId == company.Id)
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

    private static async Task<JsonElement> CreateAsync(HttpClient client, string name,
        Guid? destinationBranchId, DateOnly documentDate)
    {
        return await OkAsync(await client.PostAsJsonAsync("/api/inventory/transfers",
            new { operationId = Guid.NewGuid(), destinationName = name,
                destinationBranchId, documentDate }), HttpStatusCode.Created);
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

public sealed class I18BTransferApprovedVisualWriteFactAttribute : FactAttribute
{
    public I18BTransferApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I18B_WRITES_APPROVED") != "I18B_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
