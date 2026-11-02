using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

/// <summary>Conditional piece updates used inside the caller's stock/draft transaction.</summary>
internal static class PieceReservationTransactions
{
    public static async Task<bool> ReserveAsync(PlatformAccessDbContext db, InventoryPiece piece,
        Guid draftId, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = await db.InventoryPieces.Where(item => item.Id == piece.Id &&
                item.CompanyId == piece.CompanyId && item.BranchId == piece.BranchId &&
                item.ProductId == piece.ProductId && item.ReceivedWeightKg == piece.ReceivedWeightKg &&
                item.Status == InventoryPieceStatus.Available && item.ReservedBySaleDraftId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, InventoryPieceStatus.Reserved)
                .SetProperty(item => item.ReservedBySaleDraftId, (Guid?)draftId)
                .SetProperty(item => item.ReservedAtUtc, (DateTimeOffset?)now)
                .SetProperty(item => item.StateChangedAtUtc, now), cancellationToken);
        if (changed != 1) return false;
        db.InventoryPieceStateEvents.Add(new InventoryPieceStateEvent(piece.CompanyId, piece.BranchId,
            piece.Id, InventoryPieceStatus.Available, InventoryPieceStatus.Reserved,
            InventoryPieceEventKind.SaleReserved, draftId, actorId,
            "Reserva por borrador de venta", now));
        return true;
    }

    public static async Task<bool> ReleaseAsync(PlatformAccessDbContext db, InventoryPiece piece,
        Guid draftId, Guid actorId, InventoryPieceEventKind kind, string reason,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = await db.InventoryPieces.Where(item => item.Id == piece.Id &&
                item.CompanyId == piece.CompanyId && item.BranchId == piece.BranchId &&
                item.Status == InventoryPieceStatus.Reserved && item.ReservedBySaleDraftId == draftId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, InventoryPieceStatus.Available)
                .SetProperty(item => item.ReservedBySaleDraftId, (Guid?)null)
                .SetProperty(item => item.ReservedAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.StateChangedAtUtc, now), cancellationToken);
        if (changed != 1) return false;
        db.InventoryPieceStateEvents.Add(new InventoryPieceStateEvent(piece.CompanyId, piece.BranchId,
            piece.Id, InventoryPieceStatus.Reserved, InventoryPieceStatus.Available,
            kind, draftId, actorId, reason, now));
        return true;
    }

    public static async Task<bool> SellAsync(PlatformAccessDbContext db, InventoryPiece piece,
        Guid draftId, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = await db.InventoryPieces.Where(item => item.Id == piece.Id &&
                item.CompanyId == piece.CompanyId && item.BranchId == piece.BranchId &&
                item.Status == InventoryPieceStatus.Reserved && item.ReservedBySaleDraftId == draftId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, InventoryPieceStatus.Sold)
                .SetProperty(item => item.ReservedBySaleDraftId, (Guid?)null)
                .SetProperty(item => item.ReservedAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.StateChangedAtUtc, now), cancellationToken);
        if (changed != 1) return false;
        db.InventoryPieceStateEvents.Add(new InventoryPieceStateEvent(piece.CompanyId, piece.BranchId,
            piece.Id, InventoryPieceStatus.Reserved, InventoryPieceStatus.Sold,
            InventoryPieceEventKind.SaleConfirmed, draftId, actorId,
            "Pieza vendida", now));
        return true;
    }

    public static bool IsConcurrencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected })
                return true;
        return false;
    }

    public static bool IsDraftSaveConflict(Exception exception)
    {
        if (IsConcurrencyConflict(exception)) return true;
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: "IX_sale_drafts_CompanyId_BranchId_UserId_TicketSlot"
                })
                return true;
        return false;
    }
}
