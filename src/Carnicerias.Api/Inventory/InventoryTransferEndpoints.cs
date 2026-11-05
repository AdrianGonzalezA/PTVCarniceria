using System.Data;
using System.Text.Json;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class InventoryTransferEndpoints
{
    private static readonly JsonSerializerOptions ExportJsonOptions =
        new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapInventoryTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var transfers = endpoints.MapGroup("/api/inventory/transfers")
            .RequireOperationalPermission(PlatformPermissionCatalog.InventoryTransfersManage);
        transfers.MapGet("", ListAsync);
        transfers.MapGet("/product-options", ProductOptionsAsync);
        transfers.MapGet("/branch-options", BranchOptionsAsync);
        transfers.MapGet("/piece-options", PieceOptionsAsync);
        transfers.MapGet("/{id:guid}", GetAsync);
        transfers.MapGet("/{id:guid}/export", ExportAsync);
        transfers.MapPost("", CreateAsync);
        transfers.MapPut("/{id:guid}/lines", ReplaceLinesAsync);
        transfers.MapPost("/{id:guid}/dispatch", DispatchAsync);
        transfers.MapPost("/{id:guid}/cancel", CancelAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 20, string? status = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue ||
            status is not (null or "Draft" or "Dispatched" or "Canceled"))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var query = db.InventoryTransfers.AsNoTracking().Where(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId);
        if (status is not null)
        {
            var requested = Enum.Parse<InventoryTransferStatus>(status);
            query = query.Where(item => item.Status == requested);
        }
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new TransferSummary(item.Id, item.DocumentNumber,
                item.DocumentDate, item.DestinationName, item.Status.ToString(),
                item.Version, item.CreatedAtUtc, item.DispatchedAtUtc))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new TransferPage(items, page, pageSize, total,
            total == 0 ? 0 : (total - 1) / pageSize + 1));
    }

    private static async Task<IResult> ProductOptionsAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var items = await db.CatalogProducts.AsNoTracking()
            .Where(item => item.CompanyId == accessor.Context.CompanyId && item.IsActive)
            .OrderBy(item => item.Code).ThenBy(item => item.Id)
            .Select(item => new ProductOption(item.Id, item.Code, item.Name,
                item.Unit, item.SaleMode == ProductSaleMode.Unit ? "unit" : "weight"))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> BranchOptionsAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var items = await db.Branches.AsNoTracking().Where(item =>
                item.CompanyId == context.CompanyId && item.Id != context.BranchId && item.IsActive)
            .OrderBy(item => item.Name).Select(item => new BranchOption(item.Id, item.Name))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> PieceOptionsAsync(Guid productId, string? query,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty || query?.Length > 80) return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var pieces = db.InventoryPieces.AsNoTracking().Where(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
            item.ProductId == productId && item.Status == InventoryPieceStatus.Available);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            pieces = pieces.Where(item => item.ExternalIdentifier.Contains(term) ||
                item.RawBarcode.Contains(term));
        }
        var items = await pieces.OrderByDescending(item => item.ReceivedAtUtc)
            .ThenBy(item => item.Id).Take(50)
            .Select(item => new PieceOption(item.Id, item.ExternalIdentifier,
                item.RawBarcode, item.ReceivedWeightKg))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var transfer = await FindAsync(db, accessor, id, false, cancellationToken);
        return transfer is null ? Error(404, "TRANSFER_NOT_FOUND") :
            Results.Ok(await ToResponseAsync(db, transfer, cancellationToken));
    }

    private static async Task<IResult> ExportAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var transfer = await FindAsync(db, accessor, id, false, cancellationToken);
        if (transfer is null) return Error(404, "TRANSFER_NOT_FOUND");
        if (transfer.Status != InventoryTransferStatus.Dispatched)
            return Error(409, "TRANSFER_NOT_DISPATCHED");
        var company = await db.Companies.AsNoTracking().SingleAsync(item =>
            item.Id == transfer.CompanyId, cancellationToken);
        var branch = await db.Branches.AsNoTracking().SingleAsync(item =>
            item.CompanyId == transfer.CompanyId && item.Id == transfer.BranchId,
            cancellationToken);
        var ids = transfer.Lines.Where(line => line.PieceId.HasValue)
            .Select(line => line.PieceId!.Value).ToArray();
        var pieces = await db.InventoryPieces.AsNoTracking().Where(piece =>
                piece.CompanyId == transfer.CompanyId && ids.Contains(piece.Id))
            .ToDictionaryAsync(piece => piece.Id, cancellationToken);
        var document = new TransferExport(1, transfer.Id, transfer.OperationId,
            transfer.DocumentNumber, transfer.DocumentDate, transfer.DispatchedAtUtc!.Value,
            transfer.CompanyId, company.Code, company.Name, transfer.BranchId, branch.Name,
            transfer.DestinationBranchId, transfer.DestinationName,
            transfer.Lines.OrderBy(line => line.Id).Select(line =>
            {
                InventoryPiece? piece = line.PieceId.HasValue ? pieces[line.PieceId.Value] : null;
                return new TransferExportLine(line.Id, line.ProductId,
                    line.ProductCode, line.ProductName, line.Unit,
                    line.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
                    line.Quantity, piece?.Id, piece?.SourceSystem,
                    piece?.IdentifierField, piece?.ExternalIdentifier,
                    piece?.RawBarcode);
            }).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, ExportJsonOptions);
        return Results.File(bytes, "application/json",
            $"remito-{transfer.DocumentNumber}.json");
    }

    private static async Task<IResult> CreateAsync(CreateTransferRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.OperationId == Guid.Empty ||
            request.DocumentDate == default || string.IsNullOrWhiteSpace(request.DestinationName) ||
            request.DestinationName.Trim().Length > 200 ||
            request.DestinationBranchId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var existing = await db.InventoryTransfers.Include(item => item.Lines)
            .AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.OperationId == request.OperationId,
                cancellationToken);
        if (existing is not null)
            return existing.DestinationName == request.DestinationName.Trim() &&
                   existing.DestinationBranchId == request.DestinationBranchId &&
                   existing.DocumentDate == request.DocumentDate
                ? Results.Ok(await ToResponseAsync(db, existing, cancellationToken)) :
                    Error(409, "OPERATION_ID_REUSED");
        if (request.DestinationBranchId.HasValue)
        {
            var destination = await db.Branches.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId &&
                item.Id == request.DestinationBranchId && item.Id != context.BranchId &&
                item.IsActive, cancellationToken);
            if (destination is null) return Error(404, "DESTINATION_BRANCH_NOT_AVAILABLE");
            if (destination.Name != request.DestinationName.Trim())
                return Error(400, "DESTINATION_NAME_MISMATCH");
        }
        try
        {
            var transfer = new InventoryTransfer(context.CompanyId, context.BranchId,
                request.OperationId, request.DestinationName, request.DestinationBranchId,
                context.UserId, clock.GetUtcNow(), request.DocumentDate);
            db.InventoryTransfers.Add(transfer);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/inventory/transfers/{transfer.Id}",
                await ToResponseAsync(db, transfer, cancellationToken));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "TRANSFER_CONFLICT");
        }
    }

    private static async Task<IResult> ReplaceLinesAsync(Guid id, ReplaceLinesRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request?.Lines is null || request.Version < 1 || request.Lines.Count > 500 ||
            request.Lines.Any(line => line.ProductId == Guid.Empty || line.PieceId == Guid.Empty))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var transfer = await FindAsync(db, accessor, id, true, cancellationToken);
            if (transfer is null) return Error(404, "TRANSFER_NOT_FOUND");
            if (transfer.Status != InventoryTransferStatus.Draft)
                return Error(409, "TRANSFER_NOT_DRAFT");
            if (transfer.Version != request.Version) return Error(409, "TRANSFER_VERSION_CHANGED");
            var productIds = request.Lines.Select(line => line.ProductId).Distinct().ToArray();
            var products = await db.CatalogProducts.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && productIds.Contains(item.Id) && item.IsActive)
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (products.Count != productIds.Length) return Error(404, "PRODUCT_NOT_AVAILABLE");
            var lines = new List<InventoryTransferLine>(request.Lines.Count);
            foreach (var input in request.Lines)
            {
                try
                {
                    lines.Add(new InventoryTransferLine(transfer.Id, products[input.ProductId],
                        input.Quantity, input.PieceId));
                }
                catch (ArgumentException) { return Error(400, "INVALID_PRODUCT_QUANTITY"); }
            }
            var pieceIds = lines.Where(line => line.PieceId.HasValue)
                .Select(line => line.PieceId!.Value).Distinct().ToArray();
            var pieces = await db.InventoryPieces.AsNoTracking().Where(piece =>
                    piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId &&
                    pieceIds.Contains(piece.Id))
                .ToDictionaryAsync(piece => piece.Id, cancellationToken);
            if (pieces.Count != pieceIds.Length || lines.Where(line => line.PieceId.HasValue)
                    .Any(line => !pieces.TryGetValue(line.PieceId!.Value, out var piece) ||
                        piece.ProductId != line.ProductId ||
                        piece.ReceivedWeightKg != line.Quantity ||
                        piece.Status != InventoryPieceStatus.Available))
                return Error(409, "TRANSFER_PIECE_NOT_AVAILABLE");
            var previousLines = transfer.Lines.ToArray();
            try { transfer.ReplaceLines(lines); }
            catch (ArgumentException) { return Error(400, "DUPLICATED_TRANSFER_PIECE"); }
            db.InventoryTransferLines.RemoveRange(previousLines);
            db.InventoryTransferLines.AddRange(lines);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(await ToResponseAsync(db, transfer, cancellationToken));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "TRANSFER_VERSION_CHANGED");
        }
    }

    private static async Task<IResult> DispatchAsync(Guid id, VersionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1) return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var transfer = await FindAsync(db, accessor, id, true, cancellationToken);
            if (transfer is null) return Error(404, "TRANSFER_NOT_FOUND");
            if (transfer.Status == InventoryTransferStatus.Dispatched)
                return Results.Ok(await ToResponseAsync(db, transfer, cancellationToken));
            if (transfer.Status != InventoryTransferStatus.Draft)
                return Error(409, "TRANSFER_NOT_DRAFT");
            if (transfer.Version != request.Version) return Error(409, "TRANSFER_VERSION_CHANGED");
            if (transfer.Lines.Count == 0) return Error(400, "TRANSFER_HAS_NO_QUANTITY");
            var productIds = transfer.Lines.Select(line => line.ProductId).Distinct().ToArray();
            var products = await db.CatalogProducts.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && productIds.Contains(item.Id) && item.IsActive)
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (products.Count != productIds.Length || transfer.Lines.Any(line =>
                    products[line.ProductId].SaleMode != line.SaleMode ||
                    products[line.ProductId].Unit != line.Unit))
                return Error(409, "PRODUCT_NOT_AVAILABLE");
            var now = clock.GetUtcNow();
            foreach (var group in transfer.Lines.GroupBy(line => line.ProductId)
                         .OrderBy(group => group.Key))
            {
                if (await StockReconciliationLease.IsLockedForUpdateAsync(db, context.CompanyId,
                        context.BranchId, group.Key, now, cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
                var total = group.Sum(line => line.Quantity);
                var protectedQuantity = group.Where(line => line.PieceId.HasValue)
                    .Sum(line => line.Quantity);
                var genericQuantity = total - protectedQuantity;
                var changed = await StockReconciliationLease.Unlocked(
                        db.BranchInventoryBalances.Where(balance =>
                            balance.CompanyId == context.CompanyId &&
                            balance.BranchId == context.BranchId &&
                            balance.ProductId == group.Key &&
                            (genericQuantity == 0 || balance.OnHand -
                                balance.ProtectedOnHand - balance.Reserved +
                                balance.ProtectedReserved >= genericQuantity) &&
                            balance.ProtectedOnHand - balance.ProtectedReserved -
                                balance.Quarantined >= protectedQuantity), now)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(balance => balance.OnHand,
                            balance => balance.OnHand - total)
                        .SetProperty(balance => balance.ProtectedOnHand,
                            balance => balance.ProtectedOnHand - protectedQuantity),
                        cancellationToken);
                if (changed != 1) return Error(409, "INSUFFICIENT_TRANSFER_STOCK");
            }
            var pieceIds = transfer.Lines.Where(line => line.PieceId.HasValue)
                .Select(line => line.PieceId!.Value).ToArray();
            var pieces = await db.InventoryPieces.Where(piece =>
                    piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId &&
                    pieceIds.Contains(piece.Id))
                .ToDictionaryAsync(piece => piece.Id, cancellationToken);
            if (pieces.Count != pieceIds.Length || transfer.Lines.Where(line => line.PieceId.HasValue)
                    .Any(line => !pieces.TryGetValue(line.PieceId!.Value, out var piece) ||
                        piece.ProductId != line.ProductId ||
                        piece.Status != InventoryPieceStatus.Available ||
                        piece.ReceivedWeightKg != line.Quantity))
                return Error(409, "TRANSFER_PIECE_NOT_AVAILABLE");
            transfer.Dispatch(context.UserId, now);
            foreach (var line in transfer.Lines)
            {
                var reason = $"Despacho {transfer.DocumentNumber} a {transfer.DestinationName}";
                if (reason.Length > 240) reason = reason[..240];
                var movement = new InventoryMovement(context.CompanyId, context.BranchId,
                    line.ProductId, context.UserId, line.Id,
                    InventoryMovementKind.TransferOut, -line.Quantity, reason, now);
                db.InventoryMovements.Add(movement);
                transfer.LinkMovement(line.Id, movement.Id);
                if (line.PieceId.HasValue)
                {
                    var piece = pieces[line.PieceId.Value];
                    piece.MarkTransferred(now);
                    db.InventoryPieceStateEvents.Add(new InventoryPieceStateEvent(
                        context.CompanyId, context.BranchId, piece.Id,
                        InventoryPieceStatus.Available, InventoryPieceStatus.Transferred,
                        InventoryPieceEventKind.Transferred, null, context.UserId,
                        $"Despacho {transfer.DocumentNumber}", now));
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(await ToResponseAsync(db, transfer, cancellationToken));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "TRANSFER_CONFLICT");
        }
    }

    private static async Task<IResult> CancelAsync(Guid id, VersionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1) return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var transfer = await FindAsync(db, accessor, id, true, cancellationToken);
            if (transfer is null) return Error(404, "TRANSFER_NOT_FOUND");
            if (transfer.Status == InventoryTransferStatus.Canceled)
                return Results.Ok(await ToResponseAsync(db, transfer, cancellationToken));
            if (transfer.Status != InventoryTransferStatus.Draft)
                return Error(409, "TRANSFER_NOT_DRAFT");
            if (transfer.Version != request.Version) return Error(409, "TRANSFER_VERSION_CHANGED");
            transfer.Cancel(accessor.Context.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(await ToResponseAsync(db, transfer, cancellationToken));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "TRANSFER_CONFLICT");
        }
    }

    private static Task<InventoryTransfer?> FindAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, Guid id, bool tracked,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var query = db.InventoryTransfers.Include(item => item.Lines).AsQueryable();
        if (!tracked) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(item => item.Id == id &&
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId,
            cancellationToken);
    }

    private static async Task<TransferResponse> ToResponseAsync(PlatformAccessDbContext db,
        InventoryTransfer transfer, CancellationToken cancellationToken)
    {
        var ids = transfer.Lines.Where(line => line.PieceId.HasValue)
            .Select(line => line.PieceId!.Value).ToArray();
        var pieces = await db.InventoryPieces.AsNoTracking().Where(piece =>
                piece.CompanyId == transfer.CompanyId && ids.Contains(piece.Id))
            .Select(piece => new { piece.Id, piece.ExternalIdentifier })
            .ToDictionaryAsync(piece => piece.Id, piece => piece.ExternalIdentifier,
                cancellationToken);
        return new TransferResponse(transfer.Id, transfer.OperationId, transfer.BranchId,
            transfer.DestinationBranchId, transfer.DestinationName,
            transfer.DocumentNumber, transfer.DocumentDate, transfer.Status.ToString(),
            transfer.Version, transfer.CreatedAtUtc, transfer.DispatchedAtUtc,
            transfer.CanceledAtUtc, transfer.Lines.Select(line => new TransferLineResponse(
                line.Id, line.ProductId, line.ProductCode, line.ProductName,
                line.Unit, line.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
                line.Quantity, line.PieceId, line.PieceId.HasValue ?
                    pieces.GetValueOrDefault(line.PieceId.Value) : null,
                line.InventoryMovementId)).ToArray());
    }

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static bool IsConcurrencyConflict(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException) return true;
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
                return true;
        return false;
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo procesar el remito de transferencia", [])),
        statusCode: status);

    private sealed record CreateTransferRequest(Guid OperationId, string DestinationName,
        Guid? DestinationBranchId, DateOnly DocumentDate);
    private sealed record ReplaceLinesRequest(int Version, IReadOnlyList<TransferLineInput> Lines);
    private sealed record TransferLineInput(Guid ProductId, decimal Quantity, Guid? PieceId);
    private sealed record VersionRequest(int Version);
    private sealed record ProductOption(Guid ProductId, string Code, string Name,
        string Unit, string SaleMode);
    private sealed record BranchOption(Guid Id, string Name);
    private sealed record PieceOption(Guid Id, string ExternalIdentifier,
        string RawBarcode, decimal WeightKg);
    private sealed record TransferSummary(Guid Id, string DocumentNumber, DateOnly DocumentDate,
        string DestinationName, string Status, int Version, DateTimeOffset CreatedAtUtc,
        DateTimeOffset? DispatchedAtUtc);
    private sealed record TransferPage(IReadOnlyList<TransferSummary> Items, int Page,
        int PageSize, long TotalItems, long TotalPages);
    private sealed record TransferLineResponse(Guid Id, Guid ProductId,
        string ProductCode, string ProductName, string Unit, string SaleMode,
        decimal Quantity, Guid? PieceId, string? PieceIdentifier, Guid? InventoryMovementId);
    private sealed record TransferResponse(Guid Id, Guid OperationId, Guid BranchId,
        Guid? DestinationBranchId, string DestinationName, string DocumentNumber,
        DateOnly DocumentDate, string Status, int Version, DateTimeOffset CreatedAtUtc,
        DateTimeOffset? DispatchedAtUtc, DateTimeOffset? CanceledAtUtc,
        IReadOnlyList<TransferLineResponse> Lines);
    private sealed record TransferExport(int SchemaVersion, Guid TransferId,
        Guid OperationId, string DocumentNumber, DateOnly DocumentDate,
        DateTimeOffset DispatchedAtUtc, Guid CompanyId, string CompanyCode,
        string CompanyName, Guid OriginBranchId, string OriginBranchName,
        Guid? DestinationBranchId, string DestinationName,
        IReadOnlyList<TransferExportLine> Lines);
    private sealed record TransferExportLine(Guid LineId, Guid ProductId,
        string ProductCode, string ProductName, string Unit, string SaleMode,
        decimal Quantity, Guid? OriginPieceId, string? SourceSystem,
        string? IdentifierField, string? ExternalIdentifier, string? RawBarcode);
}
