using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class InventoryTransferReceiptTests
{
    [Fact]
    public void DestinationCreditsOnlyObservedQuantityAndKeepsDifferenceForReview()
    {
        var companyId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "CH1", "Chorizo",
            "u.", ProductSaleMode.Unit, null);
        var receipt = new InventoryTransferReceipt(companyId, destinationId, Guid.NewGuid(),
            "EMP", "Sucursal Norte", "TR-001", new DateOnly(2026, 11, 6), actorId,
            DateTimeOffset.UtcNow);
        var line = new InventoryTransferReceiptLine(receipt.Id, product, 12, 10);
        receipt.ReplaceLines([line]);

        receipt.Approve(actorId, DateTimeOffset.UtcNow);
        Assert.Equal(InventoryTransferReceiptStatus.Approved, receipt.Status);
        Assert.Equal(10, line.ReceivedQuantity);
        Assert.Equal(InventoryTransferDifferenceStatus.PendingReview, line.DifferenceStatus);
        receipt.LinkMovement(line.Id, Guid.NewGuid());
        receipt.ResolveDifference(line.Id, actorId, "Faltaron dos unidades",
            DateTimeOffset.UtcNow);
        Assert.Equal(InventoryTransferDifferenceStatus.Resolved, line.DifferenceStatus);
        Assert.Throws<InvalidOperationException>(() => receipt.Approve(actorId,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PieceRequiresPhysicalReadAndMayOmitOriginPieceId()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "2546", "Asado",
            "kg", ProductSaleMode.Weight, null);
        var receipt = new InventoryTransferReceipt(companyId, Guid.NewGuid(), Guid.NewGuid(),
            "EMP", "Sucursal Norte", "TR-002", new DateOnly(2026, 11, 6), actorId,
            DateTimeOffset.UtcNow);
        var line = new InventoryTransferReceiptLine(receipt.Id, product, 51.600m, 0,
            isPiece: true, sourceSystem: "Ciclo 2",
            sourceExternalIdentifier: "250661");
        receipt.ReplaceLines([line]);
        receipt.Approve(actorId, DateTimeOffset.UtcNow);
        Assert.Equal(InventoryTransferDifferenceStatus.PendingReview,
            line.DifferenceStatus);

        var received = new InventoryTransferReceipt(companyId, Guid.NewGuid(),
            Guid.NewGuid(), "EMP", "Sucursal Norte", "TR-003",
            new DateOnly(2026, 11, 6), actorId, DateTimeOffset.UtcNow);
        line = new InventoryTransferReceiptLine(received.Id, product, 51.600m, 0,
            isPiece: true, sourceSystem: "Ciclo 2",
            sourceExternalIdentifier: "250661");
        received.ReplaceLines([line]);

        line.CapturePiece(Guid.NewGuid(), "Ciclo 2", "pro_identif", "250661",
            "25066151600", 51.600m);
        received.Approve(actorId, DateTimeOffset.UtcNow);
        received.LinkMovement(line.Id, Guid.NewGuid(), Guid.NewGuid());
        Assert.False(line.SourcePieceVerified);
        Assert.Equal(InventoryTransferDifferenceStatus.None, line.DifferenceStatus);
    }

    [Fact]
    public void DuplicateSourceLineAndCanceledDraftAreRejected()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "CH1", "Chorizo",
            "u.", ProductSaleMode.Unit, null);
        var receipt = new InventoryTransferReceipt(companyId, Guid.NewGuid(), Guid.NewGuid(),
            "EMP", "Sucursal Norte", "TR-003", new DateOnly(2026, 11, 6), actorId,
            DateTimeOffset.UtcNow);
        var sourceLineId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => receipt.ReplaceLines([
            new InventoryTransferReceiptLine(receipt.Id, product, 2, 0,
                sourceLineId: sourceLineId),
            new InventoryTransferReceiptLine(receipt.Id, product, 3, 0,
                sourceLineId: sourceLineId)
        ]));
        receipt.Cancel(actorId, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => receipt.ReplaceLines([
            new InventoryTransferReceiptLine(receipt.Id, product, 2, 1)
        ]));
    }
}
