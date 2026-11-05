using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class InventoryTransferTests
{
    [Fact]
    public void DispatchIsFinalAndIdempotent()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "P1", "Asado", "kg",
            ProductSaleMode.Weight, null);
        var now = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
        var transfer = new InventoryTransfer(companyId, branchId, Guid.NewGuid(),
            "Sucursal no informatizada", null, actorId, now);
        var line = new InventoryTransferLine(transfer.Id, product, 2.500m);
        transfer.ReplaceLines([line]);

        transfer.Dispatch(actorId, now.AddMinutes(1));
        transfer.LinkMovement(line.Id, Guid.NewGuid());

        Assert.Equal(InventoryTransferStatus.Dispatched, transfer.Status);
        Assert.Equal(2.500m, Assert.Single(transfer.Lines).Quantity);
        Assert.Throws<InvalidOperationException>(() => transfer.ReplaceLines([]));
        Assert.Throws<InvalidOperationException>(() => transfer.Cancel(actorId, now));
        Assert.Throws<InvalidOperationException>(() => transfer.Dispatch(actorId, now));
        Assert.Throws<InvalidOperationException>(() => transfer.LinkMovement(line.Id, Guid.NewGuid()));
    }

    [Fact]
    public void TraceableLineUsesExactPieceWeightAndCannotBeDuplicated()
    {
        var companyId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "P1", "Asado", "kg",
            ProductSaleMode.Weight, null);
        var transfer = new InventoryTransfer(companyId, Guid.NewGuid(), Guid.NewGuid(),
            "Sucursal Norte", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var pieceId = Guid.NewGuid();
        var line = new InventoryTransferLine(transfer.Id, product, 51.600m, pieceId);
        transfer.ReplaceLines([line]);

        Assert.Equal(pieceId, Assert.Single(transfer.Lines).PieceId);
        Assert.Throws<ArgumentException>(() => transfer.ReplaceLines([line,
            new InventoryTransferLine(transfer.Id, product, 51.600m, pieceId)]));
        Assert.Throws<ArgumentException>(() => new InventoryTransferLine(transfer.Id,
            product, 0.001m, Guid.Empty));
    }

    [Fact]
    public void UnitProductsRequireWholeQuantityAndCanceledDraftCannotDispatch()
    {
        var companyId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "P2", "Unidad", "un",
            ProductSaleMode.Unit, null);
        var transfer = new InventoryTransfer(companyId, Guid.NewGuid(), Guid.NewGuid(),
            "Destino externo", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => new InventoryTransferLine(transfer.Id, product, 1.5m));
        transfer.ReplaceLines([new InventoryTransferLine(transfer.Id, product, 2)]);
        transfer.Cancel(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(InventoryTransferStatus.Canceled, transfer.Status);
        Assert.Throws<InvalidOperationException>(() => transfer.Dispatch(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
