using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Inventory;

public sealed record HistoricalReconciliationBalance(decimal OnHand, decimal Reserved,
    decimal Quarantined, decimal Available, decimal AdjustmentDelta);

public static class HistoricalPieceAccounting
{
    public static HistoricalReconciliationBalance? Calculate(BranchInventoryBalance balance,
        InventoryPiece piece, decimal countedOnHand)
    {
        if (piece.Status != InventoryPieceStatus.HistoricalUnverified ||
            balance.CompanyId != piece.CompanyId || balance.BranchId != piece.BranchId ||
            balance.ProductId != piece.ProductId ||
            countedOnHand < 0 || countedOnHand > 999_999_999.999m ||
            decimal.Round(countedOnHand, 3) != countedOnHand ||
            balance.Quarantined < piece.ReceivedWeightKg)
            return null;
        var remainingQuarantine = balance.Quarantined - piece.ReceivedWeightKg;
        if (countedOnHand < balance.Reserved + remainingQuarantine) return null;
        return new HistoricalReconciliationBalance(countedOnHand, balance.Reserved,
            remainingQuarantine, countedOnHand - balance.Reserved - remainingQuarantine,
            countedOnHand - balance.OnHand);
    }
}
