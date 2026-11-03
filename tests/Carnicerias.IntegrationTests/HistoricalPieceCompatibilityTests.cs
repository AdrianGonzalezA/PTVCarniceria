using Carnicerias.Api.Inventory;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class HistoricalPieceCompatibilityTests
{
    [Fact]
    public void UnverifiedHistoricalPieceCannotEnterAnyOperationalFlow()
    {
        var piece = NewPiece();
        piece.MarkHistoricalUnverified(DateTimeOffset.UtcNow);

        Assert.Equal(InventoryPieceStatus.HistoricalUnverified, piece.Status);
        Assert.Throws<InvalidOperationException>(() => piece.ReserveForSale(Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => piece.MarkTransferred(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => piece.MarkConsumed(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EvidenceBasedResolutionHasDistinctAvailableAndUnavailableOutcomes()
    {
        var now = DateTimeOffset.UtcNow;
        var found = NewPiece();
        found.MarkHistoricalUnverified(now);
        found.ReconcileHistoricalAvailable(now.AddMinutes(1));
        Assert.Equal(InventoryPieceStatus.Available, found.Status);
        found.ReserveForSale(Guid.NewGuid(), now.AddMinutes(2));

        var absent = NewPiece();
        absent.MarkHistoricalUnverified(now);
        absent.ReconcileHistoricalUnavailable(now.AddMinutes(1));
        Assert.Equal(InventoryPieceStatus.ReconciledUnavailable, absent.Status);
        Assert.Throws<InvalidOperationException>(() => absent.ReserveForSale(Guid.NewGuid(), now));
    }

    [Fact]
    public void QuarantinedQuantityIsSeparateFromBookAndDraftReservations()
    {
        var balance = new BranchInventoryBalance(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        balance.SetQuantities(62.000m, 1.000m, 0.750m);

        Assert.Equal(62.000m, balance.OnHand);
        Assert.Equal(1.000m, balance.Reserved);
        Assert.Equal(0.750m, balance.Quarantined);
        Assert.Equal(60.250m, balance.Available);
        Assert.Throws<ArgumentOutOfRangeException>(() => balance.SetQuantities(1.000m, 1.000m, 0.750m));
    }

    [Fact]
    public void ReconciliationAuditRequiresEvidenceAndDoesNotPretendThereWasASale()
    {
        var eventItem = InventoryPieceStateEvent.Reconciled(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), InventoryPieceStatus.ReconciledUnavailable, Guid.NewGuid(),
            Guid.NewGuid(), "Pieza no encontrada en conteo físico", "Acta-2026-11-02-1",
            null, 0, 61.250m, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, null);

        Assert.Equal(InventoryPieceStatus.HistoricalUnverified, eventItem.PreviousStatus);
        Assert.Equal(InventoryPieceStatus.ReconciledUnavailable, eventItem.Status);
        Assert.Equal(InventoryPieceEventKind.HistoricalReconciled, eventItem.Kind);
        Assert.Equal("Acta-2026-11-02-1", eventItem.EvidenceReference);
        Assert.Null(eventItem.SaleDraftId);
    }

    [Fact]
    public void FoundPieceReleasesQuarantineWithoutAnotherStockMovement()
    {
        var piece = NewPiece();
        piece.MarkHistoricalUnverified(DateTimeOffset.UtcNow);
        var balance = new BranchInventoryBalance(piece.CompanyId, piece.BranchId, piece.ProductId);
        balance.SetQuantities(62m, 0m, piece.ReceivedWeightKg);

        var result = HistoricalPieceAccounting.Calculate(balance, piece, 62m);

        Assert.NotNull(result);
        Assert.Equal(0m, result.Quarantined);
        Assert.Equal(62m, result.OnHand);
        Assert.Equal(62m, result.Available);
        Assert.Equal(0m, result.AdjustmentDelta);
    }

    [Fact]
    public void MissingPieceUsesPhysicalCountAndDoesNotSubtractTwice()
    {
        var piece = NewPiece(51.601m);
        piece.MarkHistoricalUnverified(DateTimeOffset.UtcNow);
        var balance = new BranchInventoryBalance(piece.CompanyId, piece.BranchId, piece.ProductId);
        balance.SetQuantities(10097.852m, 0m, 103.202m);

        var result = HistoricalPieceAccounting.Calculate(balance, piece, 10046.251m);

        Assert.NotNull(result);
        Assert.Equal(51.601m, result.Quarantined);
        Assert.Equal(-51.601m, result.AdjustmentDelta);
        Assert.Equal(9994.650m, result.Available);
        Assert.Equal(balance.Available, result.Available);
    }

    [Fact]
    public void ReconciliationRejectsCountBelowRemainingProtectedQuantity()
    {
        var piece = NewPiece();
        piece.MarkHistoricalUnverified(DateTimeOffset.UtcNow);
        var balance = new BranchInventoryBalance(piece.CompanyId, piece.BranchId, piece.ProductId);
        balance.SetQuantities(53m, 1m, piece.ReceivedWeightKg);

        Assert.Null(HistoricalPieceAccounting.Calculate(balance, piece, 0.999m));
    }

    private static InventoryPiece NewPiece(decimal weight = 51.600m) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "Frigorífico ciclo 2", Guid.NewGuid().ToString("N"),
        "2250661516008", weight, DateTimeOffset.UtcNow);
}
