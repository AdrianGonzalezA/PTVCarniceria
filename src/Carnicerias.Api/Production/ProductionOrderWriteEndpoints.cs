using System.Data;
using System.Text;
using Carnicerias.Api.Inventory;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Inventory;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Production;

public static partial class ProductionOrderEndpoints
{
    private static async Task<IResult> ReplaceLinesAsync(Guid id, ReplaceLinesRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1 || request.Lines is null ||
            request.Lines.Count > 200 || request.Lines.Any(line =>
                line.ProductId == Guid.Empty || line.InputPieceId == Guid.Empty ||
                line.OutputBarcodeProfileId == Guid.Empty ||
                !Enum.TryParse<ProductionLineDirection>(line.Direction, false, out var direction) ||
                !Enum.IsDefined(direction)))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var order = await FindAsync(db, accessor, id, cancellationToken);
            if (order is null) return Error(404, "PRODUCTION_ORDER_NOT_FOUND");
            if (order.Status != ProductionOrderStatus.Open)
                return Error(409, "PRODUCTION_ORDER_NOT_OPEN");
            if (order.Version != request.Version)
                return Error(409, "PRODUCTION_ORDER_VERSION_CHANGED");

            var productIds = request.Lines.Select(line => line.ProductId).Distinct().ToArray();
            var products = await db.CatalogProducts.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && productIds.Contains(item.Id) &&
                    item.IsActive).ToDictionaryAsync(item => item.Id, cancellationToken);
            if (products.Count != productIds.Length)
                return Error(409, "PRODUCT_NOT_AVAILABLE");
            var profileIds = request.Lines.Where(line => line.OutputBarcodeProfileId.HasValue)
                .Select(line => line.OutputBarcodeProfileId!.Value).Distinct().ToArray();
            var profiles = await db.BarcodeProfiles.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && profileIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (profiles.Count != profileIds.Length)
                return Error(409, "BARCODE_PROFILE_NOT_AVAILABLE");

            var proposed = new List<ProductionMaterialLine>(request.Lines.Count);
            foreach (var item in request.Lines)
            {
                var direction = Enum.Parse<ProductionLineDirection>(item.Direction);
                string? externalId = null;
                if (item.OutputBarcodeProfileId is Guid profileId)
                {
                    var profile = profiles[profileId];
                    try
                    {
                        var parsed = PieceBarcodeRead.Parse(profile.Formula,
                            item.OutputIdentifierField ?? string.Empty, profile.WeightField,
                            profile.WeightDecimals, item.OutputRawBarcode ?? string.Empty);
                        if (parsed.WeightKg != item.Quantity || parsed.WeightKg != item.MassKg)
                            return Error(400, "PRODUCTION_PIECE_WEIGHT_MISMATCH");
                        externalId = parsed.ExternalIdentifier;
                    }
                    catch (Exception exception) when (exception is ArgumentException or
                        FormatException or KeyNotFoundException)
                    { return Error(400, "INVALID_PRODUCTION_BARCODE"); }
                }
                try
                {
                    proposed.Add(new ProductionMaterialLine(order.Id,
                        products[item.ProductId], direction, item.Quantity, item.MassKg,
                        item.InputPieceId, item.OutputBarcodeProfileId,
                        item.OutputSourceSystem, externalId, item.OutputRawBarcode,
                        item.OutputIdentifierField));
                }
                catch (ArgumentException)
                { return Error(400, "INVALID_PRODUCTION_LINE"); }
            }
            if (proposed.Where(line => line.InputPieceId.HasValue)
                    .Select(line => line.InputPieceId).Distinct().Count() !=
                proposed.Count(line => line.InputPieceId.HasValue) ||
                proposed.Where(line => line.OutputExternalIdentifier is not null)
                    .Select(line => (line.OutputSourceSystem?.ToUpperInvariant(),
                        line.OutputExternalIdentifier)).Distinct().Count() !=
                proposed.Count(line => line.OutputExternalIdentifier is not null))
                return Error(400, "PRODUCTION_LINE_DUPLICATE");

            var oldPieces = order.Lines.Where(line => line.InputPieceId.HasValue)
                .ToDictionary(line => line.InputPieceId!.Value);
            var newPieces = proposed.Where(line => line.InputPieceId.HasValue)
                .ToDictionary(line => line.InputPieceId!.Value);
            var pieceIds = oldPieces.Keys.Union(newPieces.Keys).ToArray();
            var pieces = await db.InventoryPieces.AsNoTracking().Where(piece =>
                    piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId &&
                    pieceIds.Contains(piece.Id))
                .ToDictionaryAsync(piece => piece.Id, cancellationToken);
            if (pieces.Count != pieceIds.Length)
                return Error(409, "PRODUCTION_PIECE_NOT_AVAILABLE");
            foreach (var line in proposed.Where(line => line.InputPieceId.HasValue))
            {
                var piece = pieces[line.InputPieceId!.Value];
                if (piece.ProductId != line.ProductId || piece.ReceivedWeightKg != line.Quantity ||
                    (oldPieces.ContainsKey(piece.Id)
                        ? piece.Status != InventoryPieceStatus.Reserved ||
                          piece.ReservedByProductionOrderId != order.Id
                        : piece.Status != InventoryPieceStatus.Available))
                    return Error(409, "PRODUCTION_PIECE_NOT_AVAILABLE");
            }
            var now = clock.GetUtcNow();
            foreach (var productId in order.Lines.Select(line => line.ProductId)
                         .Union(proposed.Select(line => line.ProductId)).Distinct().Order())
                if (await StockReconciliationLease.IsLockedForUpdateAsync(db,
                        context.CompanyId, context.BranchId, productId, now,
                        cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");

            foreach (var pieceId in oldPieces.Keys.Except(newPieces.Keys).Order())
                if (!await ChangePieceReservationAsync(db, pieces[pieceId], order.Id,
                        context.UserId, now, false, cancellationToken))
                    return Error(409, "PRODUCTION_PIECE_RESERVATION_MISSING");
            foreach (var pieceId in newPieces.Keys.Except(oldPieces.Keys).Order())
                if (!await ChangePieceReservationAsync(db, pieces[pieceId], order.Id,
                        context.UserId, now, true, cancellationToken))
                    return Error(409, "PRODUCTION_PIECE_NOT_AVAILABLE");

            db.ProductionMaterialLines.RemoveRange(order.Lines);
            await db.SaveChangesAsync(cancellationToken);
            order.ReplaceLines(proposed);
            db.ProductionMaterialLines.AddRange(proposed);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(order));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        { return Error(409, "PRODUCTION_ORDER_CONFLICT"); }
    }

    private static async Task<IResult> CancelAsync(Guid id, VersionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1) return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var order = await FindAsync(db, accessor, id, cancellationToken);
            if (order is null) return Error(404, "PRODUCTION_ORDER_NOT_FOUND");
            if (order.Status == ProductionOrderStatus.Canceled)
                return Results.Ok(ToResponse(order));
            if (order.Status != ProductionOrderStatus.Open)
                return Error(409, "PRODUCTION_ORDER_NOT_OPEN");
            if (order.Version != request.Version)
                return Error(409, "PRODUCTION_ORDER_VERSION_CHANGED");
            var pieceIds = order.Lines.Where(line => line.InputPieceId.HasValue)
                .Select(line => line.InputPieceId!.Value).ToArray();
            var pieces = await db.InventoryPieces.AsNoTracking().Where(piece =>
                    piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId &&
                    pieceIds.Contains(piece.Id))
                .ToDictionaryAsync(piece => piece.Id, cancellationToken);
            if (pieces.Count != pieceIds.Length)
                return Error(409, "PRODUCTION_PIECE_RESERVATION_MISSING");
            var now = clock.GetUtcNow();
            foreach (var productId in pieces.Values.Select(piece => piece.ProductId).Distinct().Order())
                if (await StockReconciliationLease.IsLockedForUpdateAsync(db,
                        context.CompanyId, context.BranchId, productId, now,
                        cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
            foreach (var piece in pieces.Values.OrderBy(piece => piece.Id))
                if (!await ChangePieceReservationAsync(db, piece, order.Id,
                        context.UserId, now, false, cancellationToken))
                    return Error(409, "PRODUCTION_PIECE_RESERVATION_MISSING");
            order.Cancel(context.UserId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(order));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        { return Error(409, "PRODUCTION_ORDER_CONFLICT"); }
    }

    private static async Task<bool> ChangePieceReservationAsync(
        PlatformAccessDbContext db, InventoryPiece piece, Guid orderId, Guid actorId,
        DateTimeOffset now, bool reserve, CancellationToken cancellationToken)
    {
        var changed = reserve
            ? await db.InventoryPieces.Where(item => item.Id == piece.Id &&
                    item.CompanyId == piece.CompanyId && item.BranchId == piece.BranchId &&
                    item.Status == InventoryPieceStatus.Available &&
                    item.ReservedBySaleDraftId == null &&
                    item.ReservedByProductionOrderId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, InventoryPieceStatus.Reserved)
                    .SetProperty(item => item.ReservedByProductionOrderId, (Guid?)orderId)
                    .SetProperty(item => item.ReservedAtUtc, (DateTimeOffset?)now)
                    .SetProperty(item => item.StateChangedAtUtc, now), cancellationToken)
            : await db.InventoryPieces.Where(item => item.Id == piece.Id &&
                    item.CompanyId == piece.CompanyId && item.BranchId == piece.BranchId &&
                    item.Status == InventoryPieceStatus.Reserved &&
                    item.ReservedByProductionOrderId == orderId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, InventoryPieceStatus.Available)
                    .SetProperty(item => item.ReservedByProductionOrderId, (Guid?)null)
                    .SetProperty(item => item.ReservedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(item => item.StateChangedAtUtc, now), cancellationToken);
        if (changed != 1) return false;
        var quantity = piece.ReceivedWeightKg;
        var updated = await StockReconciliationLease.Unlocked(
                db.BranchInventoryBalances.Where(balance =>
                    balance.CompanyId == piece.CompanyId &&
                    balance.BranchId == piece.BranchId &&
                    balance.ProductId == piece.ProductId &&
                    (reserve
                        ? balance.ProtectedOnHand - balance.ProtectedReserved -
                            balance.Quarantined >= quantity
                        : balance.ProtectedReserved >= quantity &&
                            balance.Reserved >= quantity)), now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(balance => balance.Reserved,
                    balance => balance.Reserved + (reserve ? quantity : -quantity))
                .SetProperty(balance => balance.ProtectedReserved,
                    balance => balance.ProtectedReserved +
                        (reserve ? quantity : -quantity)), cancellationToken);
        if (updated != 1) return false;
        var stateEvent = new InventoryPieceStateEvent(piece.CompanyId, piece.BranchId,
            piece.Id, reserve ? InventoryPieceStatus.Available : InventoryPieceStatus.Reserved,
            reserve ? InventoryPieceStatus.Reserved : InventoryPieceStatus.Available,
            reserve ? InventoryPieceEventKind.ProductionReserved :
                InventoryPieceEventKind.ProductionReleased, null, actorId,
            reserve ? "Reserva por orden de producción" :
                "Liberación por edición o cancelación de producción", now);
        stateEvent.LinkProductionOrder(orderId);
        db.InventoryPieceStateEvents.Add(stateEvent);
        return true;
    }

    private sealed record ReplaceLinesRequest(int Version, IReadOnlyList<LineInput> Lines);
    private sealed record LineInput(Guid ProductId, string Direction, decimal Quantity,
        decimal MassKg, Guid? InputPieceId, Guid? OutputBarcodeProfileId,
        string? OutputSourceSystem, string? OutputRawBarcode,
        string? OutputIdentifierField);
    private sealed record VersionRequest(int Version);
}
