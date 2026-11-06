using System.Text.Json;
using Carnicerias.Api.Inventory;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class TransferRemitoImportPlannerTests
{
    [Fact]
    public void UnsignedRemitoCreatesReviewableDraftWithoutCreditingStock()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var sourcePieceId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "2546", "Asado",
            "kg", ProductSaleMode.Weight, null);
        using var json = JsonDocument.Parse(Payload(originBranchId, branchId,
            sourcePieceId));

        var plan = TransferRemitoImportPlanner.Plan(json.RootElement, companyId,
            branchId, "EMP", "Sur", [product], Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        Assert.Null(plan.Error);
        Assert.NotNull(plan.Receipt);
        Assert.Equal(InventoryTransferReceiptStatus.Draft, plan.Receipt.Status);
        Assert.NotNull(plan.Receipt.ImportedSha256);
        var line = Assert.Single(plan.Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(51.600m, line.SentQuantity);
        Assert.Equal(0, line.ReceivedQuantity);
        Assert.Equal(sourcePieceId, line.SourcePieceId);
        Assert.Equal("250661", line.SourceExternalIdentifier);
    }

    [Fact]
    public void WrongCompanyOrDestinationIsRejectedBeforeCreatingDraft()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        using var json = JsonDocument.Parse(Payload(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid()));
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "2546", "Asado",
            "kg", ProductSaleMode.Weight, null);

        var plan = TransferRemitoImportPlanner.Plan(json.RootElement, companyId,
            branchId, "OTRA", "Norte", [product], Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        Assert.Equal("REMITO_DESTINATION_OR_SCHEMA_MISMATCH", plan.Error);
        Assert.Null(plan.Receipt);
    }

    private static string Payload(Guid originBranchId, Guid destinationBranchId,
        Guid sourcePieceId) => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            transferId = Guid.NewGuid(),
            operationId = Guid.NewGuid(),
            documentNumber = "TR-001",
            documentDate = "2026-11-06",
            dispatchedAtUtc = "2026-11-06T12:00:00Z",
            companyId = Guid.NewGuid(),
            companyCode = "EMP",
            companyName = "Empresa",
            originBranchId,
            originBranchName = "Norte",
            destinationBranchId,
            destinationName = "Sur",
            lines = new[] { new
            {
                lineId = Guid.NewGuid(), productId = Guid.NewGuid(),
                productCode = "2546", productName = "Asado", unit = "kg",
                saleMode = "weight", quantity = 51.600m,
                originPieceId = sourcePieceId, sourceSystem = "Ciclo 2",
                identifierField = "pro_identif", externalIdentifier = "250661",
                rawBarcode = "25066151600"
            } }
        });
}
