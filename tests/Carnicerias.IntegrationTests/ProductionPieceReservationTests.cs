using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class ProductionPieceReservationTests
{
    [Fact]
    public void ProductionReservationExcludesSalesAndOtherOrdersUntilReleased()
    {
        var piece = CreatePiece();
        var order = Guid.NewGuid();
        var other = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        piece.ReserveForProduction(order, now);

        Assert.Equal(InventoryPieceStatus.Reserved, piece.Status);
        Assert.Equal(order, piece.ReservedByProductionOrderId);
        Assert.Throws<InvalidOperationException>(() => piece.ReserveForSale(other, now));
        Assert.Throws<InvalidOperationException>(() => piece.ReserveForProduction(other, now));
        Assert.Throws<InvalidOperationException>(() => piece.MarkConsumedByProduction(other, now));

        piece.ReleaseProductionReservation(order, now.AddSeconds(1));
        piece.ReserveForSale(other, now.AddSeconds(2));
        Assert.Null(piece.ReservedByProductionOrderId);
    }

    [Fact]
    public void ConsumedProductionPieceNeverReturnsToAvailable()
    {
        var piece = CreatePiece();
        var order = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        piece.ReserveForProduction(order, now);
        piece.MarkConsumedByProduction(order, now.AddSeconds(1));

        Assert.Equal(InventoryPieceStatus.Consumed, piece.Status);
        Assert.Throws<InvalidOperationException>(() => piece.ReserveForProduction(order, now.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => piece.ReserveForSale(Guid.NewGuid(), now.AddSeconds(2)));
    }

    private static InventoryPiece CreatePiece() => new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "FRIGO", "123456", "123456003000", 3m, DateTimeOffset.UtcNow);
}
