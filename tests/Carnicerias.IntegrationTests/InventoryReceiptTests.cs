using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class InventoryReceiptTests
{
    [Fact]
    public void ApprovesOnlyObservedQuantityAndLeavesDifferenceForAuditedResolution()
    {
        var company = Guid.NewGuid();
        var supplier = new Supplier(company, "F1", "Frigorífico Uno", "30710106513");
        var product = Product(company, ProductSaleMode.Unit);
        var actor = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var receipt = new InventoryReceipt(company, Guid.NewGuid(), Guid.NewGuid(), supplier,
            "remito", "R-25", new DateOnly(2026, 11, 5), actor, now);
        var line = new InventoryReceiptLine(receipt.Id, product, 8, 10);
        receipt.AddLine(line);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<InventoryReceiptLine>)receipt.Lines).Add(
                new InventoryReceiptLine(receipt.Id, product, 1)));
        Assert.Throws<InvalidOperationException>(() => receipt.LinkMovement(line.Id, Guid.NewGuid()));
        supplier.UpdateDetails("F1", "Nombre nuevo", null);

        receipt.Approve(actor, now.AddMinutes(1));
        var movementId = Guid.NewGuid();
        var versionBeforeMovement = receipt.Version;
        receipt.LinkMovement(line.Id, movementId);

        Assert.Equal(InventoryReceiptStatus.Approved, receipt.Status);
        Assert.Equal("Frigorífico Uno", receipt.SupplierName);
        Assert.Equal("30710106513", receipt.SupplierIdentification);
        Assert.Equal(8, line.ReceivedQuantity);
        Assert.Equal(movementId, line.InventoryMovementId);
        Assert.Equal(versionBeforeMovement + 1, receipt.Version);
        Assert.Throws<InvalidOperationException>(() => receipt.LinkMovement(line.Id, Guid.NewGuid()));
        Assert.Equal(10, line.ExpectedQuantity);
        Assert.Equal(InventoryReceiptDifferenceStatus.PendingReview, line.DifferenceStatus);
        Assert.Throws<InvalidOperationException>(() => receipt.Approve(actor, now));
        var administrator = Guid.NewGuid();
        receipt.ResolveDifference(line.Id, administrator, "Se entregaron dos unidades menos", now.AddMinutes(2));
        Assert.Equal(InventoryReceiptDifferenceStatus.Resolved, line.DifferenceStatus);
        Assert.Equal(administrator, line.DifferenceResolvedByUserId);
        Assert.Equal(8, line.ReceivedQuantity);
    }

    [Fact]
    public void CancellationNeverApprovesOrCreatesAStockMovement()
    {
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var receipt = new InventoryReceipt(company, Guid.NewGuid(), Guid.NewGuid(),
            new Supplier(company, "F1", "Proveedor"), "factura", "A-1",
            new DateOnly(2026, 11, 5), actor, DateTimeOffset.UtcNow);
        receipt.AddLine(new InventoryReceiptLine(receipt.Id, Product(company, ProductSaleMode.Weight),
            1.250m));
        receipt.Cancel(actor, DateTimeOffset.UtcNow);

        Assert.Equal(InventoryReceiptStatus.Canceled, receipt.Status);
        Assert.Null(Assert.Single(receipt.Lines).InventoryMovementId);
        Assert.Throws<InvalidOperationException>(() => receipt.Approve(actor, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void LegacyPieceHasAuditedSourceButNoInventedSupplier()
    {
        var company = Guid.NewGuid();
        var receipt = InventoryReceipt.LegacyPiece(company, Guid.NewGuid(), Guid.NewGuid(),
            "Frigorífico ciclo 2", Product(company, ProductSaleMode.Weight), 1.200m,
            Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(InventoryReceiptStatus.Approved, receipt.Status);
        Assert.Equal("legacyPiece", receipt.DocumentType);
        Assert.Null(receipt.SupplierId);
        Assert.Equal("Frigorífico ciclo 2", receipt.SourceSystem);
        Assert.Equal(1.200m, Assert.Single(receipt.Lines).ReceivedQuantity);
    }

    [Fact]
    public void RejectsOtherCompanyOrFractionalUnitQuantity()
    {
        var company = Guid.NewGuid();
        var foreign = new Supplier(Guid.NewGuid(), "F", "Foreign");
        Assert.Throws<ArgumentException>(() => new InventoryReceipt(company, Guid.NewGuid(),
            Guid.NewGuid(), foreign, "remito", "R-1", new DateOnly(2026, 11, 5),
            Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new InventoryReceiptLine(Guid.NewGuid(),
            Product(company, ProductSaleMode.Unit), 1.5m));
    }

    private static CatalogProduct Product(Guid companyId, ProductSaleMode mode) =>
        new(companyId, Guid.NewGuid(), Guid.NewGuid().ToString("N"), "Producto", mode ==
            ProductSaleMode.Unit ? "un" : "kg", mode, null);
}
