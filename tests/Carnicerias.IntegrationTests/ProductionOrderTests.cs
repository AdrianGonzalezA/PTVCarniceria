using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class ProductionOrderTests
{
    private static CatalogProduct Product(Guid company, Guid category, string code,
        ProductSaleMode mode = ProductSaleMode.Weight) =>
        new(company, category, code, code, mode == ProductSaleMode.Weight ? "kg" : "un",
            mode, null);

    [Fact]
    public void OpenOrderAcceptsMultipleInputsOutputsAndClosesWithSignedMassDifference()
    {
        var company = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var order = new ProductionOrder(company, branch, Guid.NewGuid(), actor,
            DateTimeOffset.UtcNow, Guid.NewGuid());
        var category = Guid.NewGuid();
        var beef = Product(company, category, "CARNE");
        var flour = Product(company, category, "PAN", ProductSaleMode.Unit);
        var inputBeef = new ProductionMaterialLine(order.Id, beef,
            ProductionLineDirection.Input, 20m, 20m);
        var inputFlour = new ProductionMaterialLine(order.Id, flour,
            ProductionLineDirection.Input, 1m, 10m);
        var output = new ProductionMaterialLine(order.Id, beef,
            ProductionLineDirection.Output, 31m, 31m);

        order.ReplaceLines([inputBeef, inputFlour, output]);
        order.Close(actor, 0m, null, DateTimeOffset.UtcNow);

        Assert.Equal(ProductionOrderStatus.Closed, order.Status);
        Assert.Equal(30m, order.InputMassKg);
        Assert.Equal(31m, order.OutputMassKg);
        Assert.Equal(-1m, order.MassDifferenceKg);
        Assert.Throws<InvalidOperationException>(() => order.ReplaceLines([]));
    }

    [Fact]
    public void CorrectionRequiresOriginalOrderAndTraceableOutputsRequireLabelData()
    {
        var company = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var order = new ProductionOrder(company, branch, Guid.NewGuid(), actor,
            DateTimeOffset.UtcNow, null, Guid.NewGuid());
        var beef = Product(company, Guid.NewGuid(), "LOMO");
        Assert.Equal(ProductionOrderKind.Correction, order.Kind);
        Assert.Throws<ArgumentException>(() => new ProductionMaterialLine(order.Id, beef,
            ProductionLineDirection.Output, 3m, 3m, outputBarcodeProfileId: Guid.NewGuid()));
        var piece = new ProductionMaterialLine(order.Id, beef,
            ProductionLineDirection.Output, 3m, 3m,
            outputBarcodeProfileId: Guid.NewGuid(), outputSourceSystem: "PRODUCCION",
            outputExternalIdentifier: "P-123", outputRawBarcode: "P123003000",
            outputIdentifierField: "pro_identif");
        Assert.Equal("P-123", piece.OutputExternalIdentifier);
    }

    [Fact]
    public void DuplicatePieceInputIsRejectedAndCancelPreventsClosure()
    {
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var order = new ProductionOrder(company, Guid.NewGuid(), Guid.NewGuid(), actor,
            DateTimeOffset.UtcNow, Guid.NewGuid());
        var beef = Product(company, Guid.NewGuid(), "ASADO");
        var pieceId = Guid.NewGuid();
        var first = new ProductionMaterialLine(order.Id, beef,
            ProductionLineDirection.Input, 2m, 2m, pieceId);
        var second = new ProductionMaterialLine(order.Id, beef,
            ProductionLineDirection.Input, 2m, 2m, pieceId);
        Assert.Throws<ArgumentException>(() => order.ReplaceLines([first, second]));
        order.Cancel(actor, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => order.Close(actor, 0, null,
            DateTimeOffset.UtcNow));
    }
}
