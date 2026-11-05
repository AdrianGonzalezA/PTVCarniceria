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
public sealed class InventoryReceiptVisualEndpointTests
{
    [I18AReceiptApprovedVisualWriteFact]
    [Trait("Category", "I18AApprovedDatabaseWrite")]
    public async Task DraftApprovalIsIdempotentAndOnlyReceivedQuantityEntersStock()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I18a receipt fixture {suffix}", $"I18AR-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I18a receipt");
        var role = new Role($"I18AR-{suffix}", "Receptor I18a");
        var password = $"I18AR-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i18ar-{suffix}",
            $"i18ar-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, branch.Id, "Terminal I18a");
        terminal.AssignCredentialHash(credential.Hash);
        var category = new ProductCategory(company.Id, "Mercadería I18a");
        var product = new CatalogProduct(company.Id, category.Id, $"P{suffix}",
            "Artículo I18a", "u.", ProductSaleMode.Unit, null);
        var weightProduct = new CatalogProduct(company.Id, category.Id, $"W{suffix}",
            "Pieza I18a", "kg", ProductSaleMode.Weight, null);
        var profile = new BarcodeProfile(company.Id, "I18a HTTP", 1,
            "pro_identif(6) peso(5)", "peso", 3, DateTime.UtcNow);
        var supplier = new Supplier(company.Id, $"F{suffix}", "Proveedor I18a");
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var permissionId = await db.Permissions.Where(item =>
                    item.Code == PlatformPermissionCatalog.InventoryReceiptsManage)
                    .Select(item => item.Id).SingleAsync();
                db.AddRange(company, branch, role, user,
                    new UserAssignment(user.Id, role.Id, company.Id, branch.Id),
                    new RoleCapability(role.Id, permissionId), terminal, category,
                    product, weightProduct, profile, supplier);
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
                new { companyId = company.Id, branchId = branch.Id }))
                Assert.Equal(HttpStatusCode.OK, context.StatusCode);

            var optionsResponse = await OkAsync(await client.GetAsync(
                "/api/inventory/receipts/supplier-options"));
            Assert.Equal(supplier.Id, optionsResponse[0].GetProperty("id").GetGuid());
            var productOptions = await OkAsync(await client.GetAsync(
                "/api/inventory/receipts/product-options"));
            Assert.Contains(productOptions.EnumerateArray(), item =>
                item.GetProperty("productId").GetGuid() == product.Id);
            var operationId = Guid.NewGuid();
            var documentDate = new DateOnly(2026, 11, 5);
            var created = await OkAsync(await client.PostAsJsonAsync("/api/inventory/receipts",
                new { operationId, supplierId = supplier.Id, documentType = "remito",
                    documentNumber = "R-1", documentDate }), HttpStatusCode.Created);
            var receiptId = created.GetProperty("id").GetGuid();
            Assert.Equal("Draft", created.GetProperty("status").GetString());
            var repeated = await OkAsync(await client.PostAsJsonAsync("/api/inventory/receipts",
                new { operationId, supplierId = supplier.Id, documentType = "remito",
                    documentNumber = "R-1", documentDate }));
            Assert.Equal(receiptId, repeated.GetProperty("id").GetGuid());

            await using (var db = new PlatformAccessDbContext(options))
                Assert.Equal(1, await db.InventoryReceipts.AsNoTracking()
                    .Where(item => item.Id == receiptId).Select(item => item.Version).SingleAsync());

            var edited = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/lines",
                new { version = 1, lines = new[] {
                    new { productId = product.Id, expectedQuantity = 10m, receivedQuantity = 8m }
                } }));
            Assert.Equal(2, edited.GetProperty("version").GetInt32());
            var corrected = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/lines",
                new { version = 2, lines = new[] {
                    new { productId = product.Id, expectedQuantity = 10m, receivedQuantity = 8m }
                } }));
            Assert.Equal(3, corrected.GetProperty("version").GetInt32());
            Assert.Single(corrected.GetProperty("lines").EnumerateArray());
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.False(await db.BranchInventoryBalances.AnyAsync(item =>
                    item.CompanyId == company.Id));
                Assert.False(await db.InventoryMovements.AnyAsync(item =>
                    item.CompanyId == company.Id));
            }
            var stale = await client.PutAsJsonAsync($"/api/inventory/receipts/{receiptId}/lines",
                new { version = 1, lines = Array.Empty<object>() });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var approvals = await Task.WhenAll(
                client.PostAsJsonAsync($"/api/inventory/receipts/{receiptId}/approve",
                    new { version = 3 }),
                client.PostAsJsonAsync($"/api/inventory/receipts/{receiptId}/approve",
                    new { version = 3 }));
            Assert.Contains(approvals, response => response.StatusCode == HttpStatusCode.OK);
            Assert.All(approvals, response => Assert.Contains(response.StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            foreach (var response in approvals) response.Dispose();
            var approved = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{receiptId}/approve", new { version = 3 }));
            Assert.Equal("Approved", approved.GetProperty("status").GetString());
            Assert.Equal("PendingReview", approved.GetProperty("lines")[0]
                .GetProperty("differenceStatus").GetString());

            var canceledReceipt = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/receipts", new { operationId = Guid.NewGuid(),
                    supplierId = supplier.Id, documentType = "remito",
                    documentNumber = "R-2", documentDate }), HttpStatusCode.Created);
            var canceledId = canceledReceipt.GetProperty("id").GetGuid();
            var canceled = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{canceledId}/cancel", new { version = 1 }));
            Assert.Equal("Canceled", canceled.GetProperty("status").GetString());

            await using (var db = new PlatformAccessDbContext(options))
            {
                var balance = await db.BranchInventoryBalances.AsNoTracking().SingleAsync(item =>
                    item.CompanyId == company.Id && item.BranchId == branch.Id &&
                    item.ProductId == product.Id);
                Assert.Equal(8m, balance.OnHand);
                Assert.Equal(0m, balance.Reserved);
                Assert.Equal(1, await db.InventoryMovements.CountAsync(item =>
                    item.CompanyId == company.Id && item.Kind == InventoryMovementKind.Receipt));
                Assert.Equal(2, await db.InventoryReceipts.CountAsync(item =>
                    item.CompanyId == company.Id));
            }

            var imported = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/receipts", new { operationId = Guid.NewGuid(),
                    supplierId = supplier.Id, documentType = "remito",
                    documentNumber = "R-Excel", documentDate }), HttpStatusCode.Created);
            var importedId = imported.GetProperty("id").GetGuid();
            var template = await client.GetByteArrayAsync(
                $"/api/inventory/receipts/{importedId}/import/template");
            byte[] spreadsheet;
            using (var workbook = new XLWorkbook(new MemoryStream(template)))
            {
                var sheet = workbook.Worksheet("Recepciones");
                sheet.Cell(2, 1).Value = product.Code;
                sheet.Cell(2, 2).Value = 5;
                sheet.Cell(2, 3).Value = 4;
                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                spreadsheet = stream.ToArray();
            }
            using (var preview = await client.PostAsync(
                $"/api/inventory/receipts/{importedId}/import/preview?version=1",
                SpreadsheetContent(spreadsheet)))
            {
                Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
                var body = (await preview.Content.ReadFromJsonAsync<JsonElement>());
                Assert.True(body.GetProperty("canApply").GetBoolean());
                Assert.Equal(1, body.GetProperty("createCount").GetInt32());
            }
            var savedImport = await OkAsync(await client.PostAsync(
                $"/api/inventory/receipts/{importedId}/import/apply?version=1",
                SpreadsheetContent(spreadsheet)));
            Assert.Equal(2, savedImport.GetProperty("version").GetInt32());
            await using (var db = new PlatformAccessDbContext(options))
                Assert.Equal(8m, (await db.BranchInventoryBalances.SingleAsync(item =>
                    item.CompanyId == company.Id && item.ProductId == product.Id)).OnHand);
            await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{importedId}/approve", new { version = 2 }));
            await using (var db = new PlatformAccessDbContext(options))
                Assert.Equal(12m, (await db.BranchInventoryBalances.SingleAsync(item =>
                    item.CompanyId == company.Id && item.ProductId == product.Id)).OnHand);

            var pieceOperationId = Guid.NewGuid();
            await using (var db = new PlatformAccessDbContext(options))
            {
                var stockPermissionId = await db.Permissions.Where(item =>
                    item.Code == PlatformPermissionCatalog.InventoryStockManage)
                    .Select(item => item.Id).SingleAsync();
                db.RolePermissions.Add(new RoleCapability(role.Id, stockPermissionId));
                await db.SaveChangesAsync();
            }
            var pieceRequest = new { operationId = pieceOperationId,
                productId = weightProduct.Id, barcodeProfileId = profile.Id,
                sourceSystem = "I18a HTTP fixture", identifierField = "pro_identif",
                code = "25066101600" };
            var receivedPiece = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/pieces", pieceRequest), HttpStatusCode.Created);
            Assert.Equal(1.600m, receivedPiece.GetProperty("receivedWeightKg").GetDecimal());
            var repeatedPiece = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/pieces", pieceRequest));
            Assert.Equal(receivedPiece.GetProperty("id").GetGuid(),
                repeatedPiece.GetProperty("id").GetGuid());
            await using (var db = new PlatformAccessDbContext(options))
            {
                var legacy = await db.InventoryReceipts.Include(item => item.Lines)
                    .SingleAsync(item => item.CompanyId == company.Id &&
                        item.OperationId == pieceOperationId);
                Assert.Equal(InventoryReceiptStatus.Approved, legacy.Status);
                Assert.Null(legacy.SupplierId);
                Assert.Equal("I18a HTTP fixture", legacy.SourceSystem);
                var line = Assert.Single(legacy.Lines);
                Assert.Equal(1.600m, line.ReceivedQuantity);
                Assert.NotNull(line.InventoryMovementId);
                Assert.Equal(1.600m, (await db.BranchInventoryBalances.SingleAsync(item =>
                    item.CompanyId == company.Id &&
                    item.ProductId == weightProduct.Id)).OnHand);
                Assert.Equal(1, await db.InventoryPieces.CountAsync(item =>
                    item.CompanyId == company.Id));
            }

            var pieceDraft = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/receipts", new { operationId = Guid.NewGuid(),
                    supplierId = supplier.Id, documentType = "remito",
                    documentNumber = "R-piezas", documentDate }), HttpStatusCode.Created);
            var pieceDraftId = pieceDraft.GetProperty("id").GetGuid();
            var profileOptions = await OkAsync(await client.GetAsync(
                "/api/inventory/receipts/barcode-profiles"));
            Assert.Contains(profileOptions.EnumerateArray(), item =>
                item.GetProperty("id").GetGuid() == profile.Id);
            var firstReadOperation = Guid.NewGuid();
            var firstRead = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{pieceDraftId}/pieces",
                new { version = 1, operationId = firstReadOperation,
                    productId = weightProduct.Id, barcodeProfileId = profile.Id,
                    sourceSystem = "I18a HTTP fixture", identifierField = "pro_identif",
                    code = "25066201600" }));
            Assert.Equal(2, firstRead.GetProperty("version").GetInt32());
            var repeatedRead = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{pieceDraftId}/pieces",
                new { version = 1, operationId = firstReadOperation,
                    productId = weightProduct.Id, barcodeProfileId = profile.Id,
                    sourceSystem = "I18a HTTP fixture", identifierField = "pro_identif",
                    code = "25066201600" }));
            Assert.Single(repeatedRead.GetProperty("pieceReads").EnumerateArray());
            var secondRead = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{pieceDraftId}/pieces",
                new { version = 2, operationId = Guid.NewGuid(),
                    productId = weightProduct.Id, barcodeProfileId = profile.Id,
                    sourceSystem = "I18a HTTP fixture", identifierField = "pro_identif",
                    code = "25066302400" }));
            Assert.Equal(3, secondRead.GetProperty("version").GetInt32());
            Assert.Equal(2, secondRead.GetProperty("pieceReads").GetArrayLength());
            using (var duplicateRead = await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{pieceDraftId}/pieces",
                new { version = 3, operationId = Guid.NewGuid(),
                    productId = weightProduct.Id, barcodeProfileId = profile.Id,
                    sourceSystem = "I18a HTTP fixture", identifierField = "pro_identif",
                    code = "25066201600" }))
                Assert.Equal(HttpStatusCode.Conflict, duplicateRead.StatusCode);
            await using (var db = new PlatformAccessDbContext(options))
            {
                var balance = await db.BranchInventoryBalances.SingleAsync(item =>
                    item.CompanyId == company.Id && item.ProductId == weightProduct.Id);
                Assert.Equal(1.600m, balance.OnHand);
                Assert.Equal(1.600m, balance.ProtectedOnHand);
                Assert.Equal(1, await db.InventoryPieces.CountAsync(item =>
                    item.CompanyId == company.Id));
            }
            var mixedDraft = await OkAsync(await client.PutAsJsonAsync(
                $"/api/inventory/receipts/{pieceDraftId}/lines",
                new { version = 3, lines = new[] {
                    new { productId = weightProduct.Id, expectedQuantity = (decimal?)null,
                        receivedQuantity = 0.500m }
                } }));
            Assert.Equal(4, mixedDraft.GetProperty("version").GetInt32());
            Assert.Equal(3, mixedDraft.GetProperty("lines").GetArrayLength());
            Assert.Equal(2, mixedDraft.GetProperty("pieceReads").GetArrayLength());
            byte[] pieceSpreadsheet;
            using (var workbook = new XLWorkbook(new MemoryStream(template)))
            {
                var sheet = workbook.Worksheet("Recepciones");
                sheet.Cell(2, 1).Value = weightProduct.Code;
                sheet.Cell(2, 3).Value = 0.500;
                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                pieceSpreadsheet = stream.ToArray();
            }
            var importedWithPieces = await OkAsync(await client.PostAsync(
                $"/api/inventory/receipts/{pieceDraftId}/import/apply?version=4",
                SpreadsheetContent(pieceSpreadsheet)));
            Assert.Equal(5, importedWithPieces.GetProperty("version").GetInt32());
            Assert.Equal(3, importedWithPieces.GetProperty("lines").GetArrayLength());
            Assert.Equal(2, importedWithPieces.GetProperty("pieceReads").GetArrayLength());
            var competingDraft = await OkAsync(await client.PostAsJsonAsync(
                "/api/inventory/receipts", new { operationId = Guid.NewGuid(),
                    supplierId = supplier.Id, documentType = "remito",
                    documentNumber = "R-pieza-duplicada", documentDate }), HttpStatusCode.Created);
            var competingId = competingDraft.GetProperty("id").GetGuid();
            var competingScan = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{competingId}/pieces",
                new { version = 1, operationId = Guid.NewGuid(),
                    productId = weightProduct.Id, barcodeProfileId = profile.Id,
                    sourceSystem = "I18a HTTP fixture", identifierField = "pro_identif",
                    code = "25066201600" }));
            Assert.Equal(2, competingScan.GetProperty("version").GetInt32());
            var pieceApproved = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{pieceDraftId}/approve", new { version = 5 }));
            Assert.Equal("Approved", pieceApproved.GetProperty("status").GetString());
            var approvedAgain = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{pieceDraftId}/approve", new { version = 5 }));
            Assert.Equal(pieceApproved.GetProperty("id").GetGuid(),
                approvedAgain.GetProperty("id").GetGuid());
            using (var duplicateApproval = await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{competingId}/approve", new { version = 2 }))
                Assert.Equal(HttpStatusCode.Conflict, duplicateApproval.StatusCode);
            var canceledCompeting = await OkAsync(await client.PostAsJsonAsync(
                $"/api/inventory/receipts/{competingId}/cancel", new { version = 2 }));
            Assert.Equal("Canceled", canceledCompeting.GetProperty("status").GetString());
            await using (var db = new PlatformAccessDbContext(options))
            {
                var balance = await db.BranchInventoryBalances.SingleAsync(item =>
                    item.CompanyId == company.Id && item.ProductId == weightProduct.Id);
                Assert.Equal(6.100m, balance.OnHand);
                Assert.Equal(5.600m, balance.ProtectedOnHand);
                Assert.Equal(0.500m, balance.GenericOnHand);
                Assert.Equal(3, await db.InventoryPieces.CountAsync(item =>
                    item.CompanyId == company.Id));
                Assert.Equal(2, await db.InventoryReceiptPieceReads.CountAsync(item =>
                    item.CompanyId == company.Id && item.InventoryPieceId != null));
                Assert.Equal(3, await db.InventoryMovements.CountAsync(item =>
                    item.CompanyId == company.Id && item.Kind == InventoryMovementKind.PieceReceipt));
            }
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.InventoryReceiptPieceReads.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryReceiptLines.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryReceipts.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryPieceStateEvents.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryPieces.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.InventoryMovements.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.BranchInventoryBalances.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Suppliers.Where(item => item.CompanyId == company.Id)
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

public sealed class I18AReceiptApprovedVisualWriteFactAttribute : FactAttribute
{
    public I18AReceiptApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I18A_WRITES_APPROVED") != "I18A_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
