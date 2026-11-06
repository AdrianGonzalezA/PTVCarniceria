using System.Data;
using System.Text;
using Carnicerias.Api.Inventory;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Production;

public static partial class ProductionOrderEndpoints
{
    private static async Task<IResult> CloseAsync(Guid id, CloseOrderRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1 || request.ReportedWasteKg < 0 ||
            request.ReportedWasteKg > 100_000 ||
            decimal.Round(request.ReportedWasteKg, 3) != request.ReportedWasteKg ||
            request.WasteReason?.Trim().Length > 240 ||
            request.ReportedWasteKg > 0 && string.IsNullOrWhiteSpace(request.WasteReason))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var order = await FindAsync(db, accessor, id, cancellationToken);
            if (order is null) return Error(404, "PRODUCTION_ORDER_NOT_FOUND");
            if (order.Status == ProductionOrderStatus.Closed)
                return order.ReportedWasteKg == request.ReportedWasteKg &&
                       order.WasteReason == (request.WasteReason?.Trim() ?? string.Empty)
                    ? Results.Ok(ToResponse(order)) :
                    Error(409, "PRODUCTION_ORDER_ALREADY_CLOSED");
            if (order.Status != ProductionOrderStatus.Open)
                return Error(409, "PRODUCTION_ORDER_NOT_OPEN");
            if (order.Version != request.Version)
                return Error(409, "PRODUCTION_ORDER_VERSION_CHANGED");
            if (!order.Lines.Any(line => line.Direction == ProductionLineDirection.Input) ||
                !order.Lines.Any(line => line.Direction == ProductionLineDirection.Output))
                return Error(400, "PRODUCTION_ORDER_LINES_REQUIRED");
            var now = clock.GetUtcNow();
            foreach (var productId in order.Lines.Select(line => line.ProductId).Distinct().Order())
                if (await StockReconciliationLease.IsLockedForUpdateAsync(db,
                        context.CompanyId, context.BranchId, productId, now,
                        cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");

            var inputPieceIds = order.Lines.Where(line => line.InputPieceId.HasValue)
                .Select(line => line.InputPieceId!.Value).ToArray();
            var pieces = await db.InventoryPieces.AsNoTracking().Where(piece =>
                    piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId &&
                    inputPieceIds.Contains(piece.Id))
                .ToDictionaryAsync(piece => piece.Id, cancellationToken);
            if (pieces.Count != inputPieceIds.Length || order.Lines
                    .Where(line => line.InputPieceId.HasValue).Any(line =>
                        !pieces.TryGetValue(line.InputPieceId!.Value, out var piece) ||
                        piece.ProductId != line.ProductId ||
                        piece.ReceivedWeightKg != line.Quantity ||
                        piece.Status != InventoryPieceStatus.Reserved ||
                        piece.ReservedByProductionOrderId != order.Id))
                return Error(409, "PRODUCTION_PIECE_RESERVATION_MISSING");

            var outputPieces = order.Lines.Where(line => line.OutputBarcodeProfileId.HasValue)
                .ToArray();
            var sources = outputPieces.Select(line => line.OutputSourceSystem!.Trim()
                .Normalize(NormalizationForm.FormKC).ToUpperInvariant()).Distinct().ToArray();
            var identifiers = outputPieces.Select(line => line.OutputExternalIdentifier!)
                .Distinct().ToArray();
            var usedIdentifiers = await db.InventoryPieces.AsNoTracking().Where(piece =>
                    piece.CompanyId == context.CompanyId &&
                    sources.Contains(piece.NormalizedSourceSystem) &&
                    identifiers.Contains(piece.ExternalIdentifier))
                .Select(piece => new { piece.NormalizedSourceSystem,
                    piece.ExternalIdentifier }).ToArrayAsync(cancellationToken);
            if (usedIdentifiers.Any(piece => outputPieces.Any(line =>
                    string.Equals(line.OutputSourceSystem!.Trim().Normalize(
                        NormalizationForm.FormKC), piece.NormalizedSourceSystem,
                        StringComparison.OrdinalIgnoreCase) &&
                    line.OutputExternalIdentifier == piece.ExternalIdentifier)))
                return Error(409, "PRODUCTION_PIECE_IDENTIFIER_USED");

            order.Close(context.UserId, request.ReportedWasteKg, request.WasteReason, now);
            foreach (var line in order.Lines.Where(line =>
                         line.Direction == ProductionLineDirection.Input))
            {
                if (line.InputPieceId is Guid pieceId)
                {
                    var piece = pieces[pieceId];
                    var changed = await db.InventoryPieces.Where(item => item.Id == pieceId &&
                            item.CompanyId == context.CompanyId &&
                            item.BranchId == context.BranchId &&
                            item.Status == InventoryPieceStatus.Reserved &&
                            item.ReservedByProductionOrderId == order.Id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.Status,
                                InventoryPieceStatus.Consumed)
                            .SetProperty(item => item.ReservedByProductionOrderId,
                                (Guid?)null)
                            .SetProperty(item => item.ReservedAtUtc,
                                (DateTimeOffset?)null)
                            .SetProperty(item => item.StateChangedAtUtc, now),
                            cancellationToken);
                    if (changed != 1)
                        return Error(409, "PRODUCTION_PIECE_RESERVATION_MISSING");
                    var stockChanged = await StockReconciliationLease.Unlocked(
                            db.BranchInventoryBalances.Where(balance =>
                                balance.CompanyId == context.CompanyId &&
                                balance.BranchId == context.BranchId &&
                                balance.ProductId == line.ProductId &&
                                balance.Reserved >= line.Quantity &&
                                balance.ProtectedReserved >= line.Quantity &&
                                balance.ProtectedOnHand - line.Quantity >=
                                    balance.Quarantined), now)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(balance => balance.OnHand,
                                balance => balance.OnHand - line.Quantity)
                            .SetProperty(balance => balance.Reserved,
                                balance => balance.Reserved - line.Quantity)
                            .SetProperty(balance => balance.ProtectedOnHand,
                                balance => balance.ProtectedOnHand - line.Quantity)
                            .SetProperty(balance => balance.ProtectedReserved,
                                balance => balance.ProtectedReserved - line.Quantity),
                            cancellationToken);
                    if (stockChanged != 1)
                        return Error(409, "PRODUCTION_STOCK_RESERVATION_MISSING");
                    var stateEvent = new InventoryPieceStateEvent(context.CompanyId,
                        context.BranchId, pieceId, InventoryPieceStatus.Reserved,
                        InventoryPieceStatus.Consumed,
                        InventoryPieceEventKind.ProductionConsumed, null,
                        context.UserId, "Pieza consumida en producción", now);
                    stateEvent.LinkProductionOrder(order.Id);
                    db.InventoryPieceStateEvents.Add(stateEvent);
                }
                else if (!await ChangeGenericStockAsync(db, context.CompanyId,
                             context.BranchId, line.ProductId, -line.Quantity, 0,
                             now, cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
                AddMovement(db, order, line, context.UserId,
                    InventoryMovementKind.ProductionInput, -line.Quantity, now);
            }
            foreach (var line in order.Lines.Where(line =>
                         line.Direction == ProductionLineDirection.Output))
            {
                var protectedDelta = line.OutputBarcodeProfileId.HasValue ? line.Quantity : 0;
                if (!await ChangeGenericStockAsync(db, context.CompanyId,
                        context.BranchId, line.ProductId, line.Quantity, protectedDelta,
                        now, cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
                var movement = AddMovement(db, order, line, context.UserId,
                    InventoryMovementKind.ProductionOutput, line.Quantity, now,
                    linkImmediately: !line.OutputBarcodeProfileId.HasValue);
                if (line.OutputBarcodeProfileId is not Guid profileId) continue;
                var piece = new InventoryPiece(context.CompanyId, context.BranchId,
                    line.ProductId, profileId, context.UserId, line.Id,
                    movement.Id, line.OutputSourceSystem!,
                    line.OutputExternalIdentifier!, line.OutputRawBarcode!,
                    line.Quantity, now, line.OutputIdentifierField!,
                    productionOrderId: order.Id);
                db.InventoryPieces.Add(piece);
                order.LinkMovement(line.Id, movement.Id, piece.Id);
                var stateEvent = new InventoryPieceStateEvent(context.CompanyId,
                    context.BranchId, piece.Id, null, InventoryPieceStatus.Available,
                    InventoryPieceEventKind.Produced, null, context.UserId,
                    "Pieza obtenida por producción", now);
                stateEvent.LinkProductionOrder(order.Id);
                db.InventoryPieceStateEvents.Add(stateEvent);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(order));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        { return Error(409, "PRODUCTION_ORDER_CONFLICT"); }
    }

    private static InventoryMovement AddMovement(PlatformAccessDbContext db,
        ProductionOrder order, ProductionMaterialLine line, Guid userId,
        InventoryMovementKind kind, decimal delta, DateTimeOffset now,
        bool linkImmediately = true)
    {
        var movement = new InventoryMovement(order.CompanyId, order.BranchId,
            line.ProductId, userId, line.Id, kind, delta,
            kind == InventoryMovementKind.ProductionInput
                ? "Consumo por orden de producción" :
                  "Obtención por orden de producción", now);
        db.InventoryMovements.Add(movement);
        if (linkImmediately) order.LinkMovement(line.Id, movement.Id);
        return movement;
    }

    private static async Task<bool> ChangeGenericStockAsync(PlatformAccessDbContext db,
        Guid companyId, Guid branchId, Guid productId, decimal delta,
        decimal protectedDelta, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventory.branch_inventory
                ("CompanyId", "BranchId", "ProductId", "OnHand", "Reserved",
                 "ProtectedOnHand", "ProtectedReserved")
            VALUES ({companyId}, {branchId}, {productId}, {delta}, 0,
                    {protectedDelta}, 0)
            ON CONFLICT ("CompanyId", "BranchId", "ProductId") DO UPDATE
                SET "OnHand" = inventory.branch_inventory."OnHand" + EXCLUDED."OnHand",
                    "ProtectedOnHand" = inventory.branch_inventory."ProtectedOnHand" +
                        EXCLUDED."ProtectedOnHand"
            WHERE inventory.branch_inventory."ReconciliationLockExpiresAtUtc" IS NULL OR
                inventory.branch_inventory."ReconciliationLockExpiresAtUtc" <= {now}
            """, cancellationToken);
        return changed == 1;
    }

    private sealed record CloseOrderRequest(int Version, decimal ReportedWasteKg,
        string? WasteReason);
}
