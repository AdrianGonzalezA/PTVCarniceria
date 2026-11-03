using System.Data;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Inventory;

public static class PieceHistoricalReconciliationEndpoints
{
    public static RouteGroupBuilder MapHistoricalReconciliation(this RouteGroupBuilder pieces)
    {
        pieces.MapPost("/{id:guid}/reconciliation-lease", AcquireAsync);
        pieces.MapPost("/{id:guid}/reconciliation-lease/renew", RenewAsync);
        pieces.MapDelete("/{id:guid}/reconciliation-lease/{lockId:guid}", ReleaseAsync);
        pieces.MapPost("/{id:guid}/reconcile", ReconcileAsync);
        return pieces;
    }

    private static async Task<IResult> AcquireAsync(Guid id, LeaseRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (id == Guid.Empty || request is null || request.LockId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (!await IsCompanyAdminAsync(db, context.CompanyId, context.UserId, cancellationToken))
            return Error(403, "ADMINISTRATOR_REQUIRED");
        var now = clock.GetUtcNow();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var piece = await db.InventoryPieces.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == id && item.CompanyId == context.CompanyId && item.BranchId == context.BranchId,
                cancellationToken);
            if (piece is null) return Error(404, "PIECE_NOT_FOUND");
            if (piece.Status != InventoryPieceStatus.HistoricalUnverified)
                return Error(409, "PIECE_NOT_PENDING_RECONCILIATION");

            var balance = await LockBalanceAsync(db, piece, cancellationToken);
            if (balance is null) return Error(409, "STOCK_BALANCE_MISSING");
            now = clock.GetUtcNow();
            if (balance.IsReconciliationLocked(now))
                return balance.ReconciliationLockId == request!.LockId &&
                    balance.ReconciliationLockPieceId == id &&
                    balance.ReconciliationLockOwnerUserId == context.UserId
                    ? Results.Ok(ToLease(balance))
                    : Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");

            var expires = now.Add(StockReconciliationLease.Duration);
            var changed = await db.BranchInventoryBalances.Where(item =>
                    item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                    item.ProductId == piece.ProductId &&
                    (item.ReconciliationLockExpiresAtUtc == null ||
                     item.ReconciliationLockExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ReconciliationLockId, (Guid?)request!.LockId)
                    .SetProperty(item => item.ReconciliationLockPieceId, (Guid?)id)
                    .SetProperty(item => item.ReconciliationLockOwnerUserId, (Guid?)context.UserId)
                    .SetProperty(item => item.ReconciliationLockStartedAtUtc, (DateTimeOffset?)now)
                    .SetProperty(item => item.ReconciliationLockExpiresAtUtc, (DateTimeOffset?)expires),
                    cancellationToken);
            if (changed != 1) return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
            if (await db.SaleDrafts.AsNoTracking().AnyAsync(draft =>
                    draft.CompanyId == context.CompanyId && draft.BranchId == context.BranchId &&
                    draft.Status == SaleDraftStatus.Draft && db.Set<SaleDraftLine>().Any(line =>
                        line.SaleDraftId == draft.Id && line.ProductId == piece.ProductId),
                    cancellationToken))
                return Error(409, "ARTICLE_HAS_OPEN_TICKETS");

            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new LeaseResponse(request!.LockId, id, now, expires));
        }
        catch (Exception exception) when (PieceReservationTransactions.IsConcurrencyConflict(exception))
        {
            return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
        }
    }

    private static async Task<IResult> RenewAsync(Guid id, LeaseRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (id == Guid.Empty || request is null || request.LockId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (!await IsCompanyAdminAsync(db, context.CompanyId, context.UserId, cancellationToken))
            return Error(403, "ADMINISTRATOR_REQUIRED");
        var now = clock.GetUtcNow();
        var expires = now.Add(StockReconciliationLease.Duration);
        var changed = await db.BranchInventoryBalances.Where(balance =>
                balance.CompanyId == context.CompanyId && balance.BranchId == context.BranchId &&
                balance.ReconciliationLockPieceId == id &&
                balance.ReconciliationLockId == request.LockId &&
                balance.ReconciliationLockOwnerUserId == context.UserId &&
                balance.ReconciliationLockExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                balance => balance.ReconciliationLockExpiresAtUtc, (DateTimeOffset?)expires),
                cancellationToken);
        return changed == 1 ? Results.Ok(new { lockId = request.LockId, expiresAtUtc = expires }) :
            Error(409, "RECONCILIATION_LEASE_EXPIRED");
    }

    private static async Task<IResult> ReleaseAsync(Guid id, Guid lockId,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (id == Guid.Empty || lockId == Guid.Empty) return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (!await IsCompanyAdminAsync(db, context.CompanyId, context.UserId, cancellationToken))
            return Error(403, "ADMINISTRATOR_REQUIRED");
        var changed = await db.BranchInventoryBalances.Where(balance =>
                balance.CompanyId == context.CompanyId && balance.BranchId == context.BranchId &&
                balance.ReconciliationLockPieceId == id &&
                balance.ReconciliationLockId == lockId &&
                balance.ReconciliationLockOwnerUserId == context.UserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(balance => balance.ReconciliationLockId, (Guid?)null)
                .SetProperty(balance => balance.ReconciliationLockPieceId, (Guid?)null)
                .SetProperty(balance => balance.ReconciliationLockOwnerUserId, (Guid?)null)
                .SetProperty(balance => balance.ReconciliationLockStartedAtUtc, (DateTimeOffset?)null)
                .SetProperty(balance => balance.ReconciliationLockExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken);
        return changed == 1 ? Results.NoContent() : Error(409, "RECONCILIATION_LEASE_MISSING");
    }

    private static async Task<IResult> ReconcileAsync(Guid id, ReconcileRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        var now = clock.GetUtcNow();
        if (id == Guid.Empty || request is null || request.LockId == Guid.Empty ||
            request.OperationId == Guid.Empty || request.Outcome is not ("available" or "unavailable") ||
            request.ObservedWeightKg < 0 || request.CountedOnHand < 0 ||
            decimal.Round(request.ObservedWeightKg, 3) != request.ObservedWeightKg ||
            decimal.Round(request.CountedOnHand, 3) != request.CountedOnHand ||
            request.CountedOnHand > 999_999_999.999m ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 240 ||
            string.IsNullOrWhiteSpace(request.EvidenceReference) ||
            request.EvidenceReference.Trim().Length > 512 ||
            request.EvidenceSha256 is not null && (request.EvidenceSha256.Length != 64 ||
                !request.EvidenceSha256.All(Uri.IsHexDigit)))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (!await IsCompanyAdminAsync(db, context.CompanyId, context.UserId, cancellationToken))
            return Error(403, "ADMINISTRATOR_REQUIRED");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var existing = await db.InventoryPieceStateEvents.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId &&
                item.ReconciliationOperationId == request.OperationId, cancellationToken);
            if (existing is not null)
                return existing.PieceId == id && existing.EvidenceReference ==
                    request.EvidenceReference.Trim() && existing.Status ==
                    (request.Outcome == "available" ? InventoryPieceStatus.Available :
                        InventoryPieceStatus.ReconciledUnavailable) &&
                    existing.Reason == request.Reason.Trim() &&
                    existing.ObservedWeightKg == request.ObservedWeightKg &&
                    existing.CountedOnHand == request.CountedOnHand &&
                    existing.EvidenceSha256 == request.EvidenceSha256?.ToUpperInvariant()
                    ? Results.Ok(new { pieceId = id, status = existing.Status.ToString(),
                        eventId = existing.Id, alreadyApplied = true })
                    : Error(409, "RECONCILIATION_OPERATION_REUSED");

            var current = await db.InventoryPieces.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == id && item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId, cancellationToken);
            if (current is null) return Error(404, "PIECE_NOT_FOUND");
            var balance = await LockBalanceAsync(db, current, cancellationToken);
            now = clock.GetUtcNow();
            if (balance is null || balance.ReconciliationLockId != request.LockId ||
                balance.ReconciliationLockPieceId != id ||
                balance.ReconciliationLockOwnerUserId != context.UserId ||
                balance.ReconciliationLockExpiresAtUtc <= now)
                return Error(409, "RECONCILIATION_LEASE_EXPIRED");
            var pieces = await db.InventoryPieces.FromSqlInterpolated($"""
                SELECT * FROM inventory.pieces WHERE "Id" = {id} AND
                    "CompanyId" = {context.CompanyId} AND "BranchId" = {context.BranchId}
                FOR UPDATE
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (pieces.Length != 1) return Error(404, "PIECE_NOT_FOUND");
            var piece = pieces[0];
            if (piece.Status != InventoryPieceStatus.HistoricalUnverified)
                return Error(409, "PIECE_NOT_PENDING_RECONCILIATION");
            if (piece.ProductId != current.ProductId)
                return Error(409, "PIECE_OPERATION_IN_PROGRESS");
            if (await db.ConfirmedSaleLines.AsNoTracking().AnyAsync(line =>
                    line.InventoryPieceId == id, cancellationToken) ||
                await db.Set<SaleDraftLine>().AsNoTracking().AnyAsync(line =>
                    line.InventoryPieceId == id && db.SaleDrafts.Any(draft =>
                        draft.Id == line.SaleDraftId && draft.Status == SaleDraftStatus.Draft),
                    cancellationToken))
                return Error(409, "PIECE_ALREADY_USED");
            if (request.ObservedWeightKg > piece.ReceivedWeightKg ||
                request.Outcome == "available" && request.ObservedWeightKg != piece.ReceivedWeightKg)
                return Error(400, "PIECE_WEIGHT_NOT_VERIFIED");
            var updatedStock = HistoricalPieceAccounting.Calculate(balance, piece, request.CountedOnHand);
            if (updatedStock is null)
                return Error(409, "STOCK_COUNT_BELOW_PROTECTED");

            var outcome = request.Outcome == "available" ? InventoryPieceStatus.Available :
                InventoryPieceStatus.ReconciledUnavailable;
            var changedPiece = await db.InventoryPieces.Where(item => item.Id == id &&
                    item.Status == InventoryPieceStatus.HistoricalUnverified)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, outcome)
                    .SetProperty(item => item.StateChangedAtUtc, now), cancellationToken);
            if (changedPiece != 1) return Error(409, "PIECE_OPERATION_IN_PROGRESS");

            var changedBalance = await db.BranchInventoryBalances.Where(item =>
                    item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                    item.ProductId == piece.ProductId && item.ReconciliationLockId == request.LockId &&
                    item.ReconciliationLockExpiresAtUtc > now &&
                    item.Quarantined >= piece.ReceivedWeightKg)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.OnHand, request.CountedOnHand)
                    .SetProperty(item => item.Quarantined,
                        item => item.Quarantined - piece.ReceivedWeightKg)
                    .SetProperty(item => item.ReconciliationLockId, (Guid?)null)
                    .SetProperty(item => item.ReconciliationLockPieceId, (Guid?)null)
                    .SetProperty(item => item.ReconciliationLockOwnerUserId, (Guid?)null)
                    .SetProperty(item => item.ReconciliationLockStartedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(item => item.ReconciliationLockExpiresAtUtc, (DateTimeOffset?)null),
                    cancellationToken);
            if (changedBalance != 1) return Error(409, "RECONCILIATION_LEASE_EXPIRED");

            InventoryMovement? movement = null;
            var delta = updatedStock.AdjustmentDelta;
            if (delta != 0)
            {
                movement = new InventoryMovement(context.CompanyId, context.BranchId,
                    piece.ProductId, context.UserId, request.OperationId,
                    InventoryMovementKind.Adjustment, delta,
                    $"Conciliación histórica de pieza {piece.ExternalIdentifier}", now);
                db.InventoryMovements.Add(movement);
            }
            var stateEvent = InventoryPieceStateEvent.Reconciled(context.CompanyId,
                context.BranchId, id, outcome, context.UserId, request.OperationId,
                request.Reason.Trim(), request.EvidenceReference.Trim(), request.EvidenceSha256,
                request.ObservedWeightKg, request.CountedOnHand, now,
                now, movement?.Id);
            db.InventoryPieceStateEvents.Add(stateEvent);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { pieceId = id, status = outcome.ToString(),
                eventId = stateEvent.Id, alreadyApplied = false });
        }
        catch (Exception exception) when (PieceReservationTransactions.IsConcurrencyConflict(exception))
        {
            return Error(409, "PIECE_OPERATION_IN_PROGRESS");
        }
    }

    private static async Task<BranchInventoryBalance?> LockBalanceAsync(
        PlatformAccessDbContext db, InventoryPiece piece, CancellationToken cancellationToken)
    {
        var rows = await db.BranchInventoryBalances.FromSqlInterpolated($"""
            SELECT * FROM inventory.branch_inventory WHERE "CompanyId" = {piece.CompanyId} AND
                "BranchId" = {piece.BranchId} AND "ProductId" = {piece.ProductId}
            FOR UPDATE
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private static Task<bool> IsCompanyAdminAsync(PlatformAccessDbContext db, Guid companyId,
        Guid userId, CancellationToken cancellationToken) =>
        (from assignment in db.UserAssignments.AsNoTracking()
         join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
         where assignment.UserId == userId && assignment.CompanyId == companyId &&
               assignment.BranchId == null && role.Code == "administrator"
         select assignment.UserId).AnyAsync(cancellationToken);

    private static bool AllowedOrigin(HttpContext http) =>
        string.IsNullOrEmpty(http.Request.Headers.Origin.ToString()) ||
        RequestOriginValidator.IsAllowed(http.Request.Headers.Origin.ToString(), http.Request);

    private static LeaseResponse ToLease(BranchInventoryBalance balance) => new(
        balance.ReconciliationLockId!.Value, balance.ReconciliationLockPieceId!.Value,
        balance.ReconciliationLockStartedAtUtc!.Value,
        balance.ReconciliationLockExpiresAtUtc!.Value);

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: status);

    private sealed record LeaseRequest(Guid LockId);
    private sealed record LeaseResponse(Guid LockId, Guid PieceId,
        DateTimeOffset StartedAtUtc, DateTimeOffset ExpiresAtUtc);
    private sealed record ReconcileRequest(Guid LockId, Guid OperationId, string Outcome,
        decimal ObservedWeightKg, decimal CountedOnHand,
        string Reason, string EvidenceReference, string? EvidenceSha256);
}
