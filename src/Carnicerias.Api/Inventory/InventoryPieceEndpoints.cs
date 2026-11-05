using System.Data;
using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Api.Sales;
using Carnicerias.Domain.Inventory;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class InventoryPieceEndpoints
{
    public static IEndpointRouteBuilder MapInventoryPieceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var pieces = endpoints.MapGroup("/api/inventory/pieces")
            .RequireOperationalPermission(PlatformPermissionCatalog.InventoryStockManage);
        pieces.MapGet("", ListAsync);
        pieces.MapGet("/{id:guid}", GetAsync);
        pieces.MapGet("/{id:guid}/events", ListEventsAsync);
        pieces.MapPost("", ReceiveAsync);
        pieces.MapPost("/{id:guid}/release", ReleaseReservedAsync);
        pieces.MapHistoricalReconciliation();
        endpoints.MapGet("/api/pos/pieces/lookup", LookupForPosAsync).RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> LookupForPosAsync(string? code, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var barcode = code?.Trim();
        if (string.IsNullOrEmpty(barcode) || barcode.Length > 80)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var context = accessor.Context;
        var matches = await (from piece in db.InventoryPieces.AsNoTracking()
                             join product in db.CatalogProducts.AsNoTracking()
                                 on piece.ProductId equals product.Id
                             where piece.CompanyId == context.CompanyId &&
                                 piece.BranchId == context.BranchId && piece.RawBarcode == barcode &&
                                 product.CompanyId == context.CompanyId
                             select new
                             {
                                 piece.Id,
                                 piece.Status,
                                 piece.ProductId,
                                 ProductCode = product.Code,
                                 piece.ExternalIdentifier,
                                 piece.ReceivedWeightKg,
                                 piece.RawBarcode,
                                 product.IsActive,
                                 product.SaleMode
                             })
            .Take(2).ToArrayAsync(cancellationToken);
        if (matches.Length == 1 && await db.ConfirmedSaleLines.AnyAsync(line =>
                line.InventoryPieceId == matches[0].Id, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "PIECE_ALREADY_SOLD");
        return matches.Length switch
        {
            0 => Error(StatusCodes.Status404NotFound, "PIECE_NOT_FOUND"),
            > 1 => Error(StatusCodes.Status409Conflict, "PIECE_BARCODE_AMBIGUOUS"),
            _ when matches[0].Status == InventoryPieceStatus.Sold =>
                Error(StatusCodes.Status409Conflict, "PIECE_ALREADY_SOLD"),
            _ when matches[0].Status == InventoryPieceStatus.Reserved =>
                Error(StatusCodes.Status409Conflict, "PIECE_RESERVED"),
            _ when matches[0].Status == InventoryPieceStatus.HistoricalUnverified =>
                Error(StatusCodes.Status409Conflict, "PIECE_RECONCILIATION_REQUIRED"),
            _ when matches[0].Status != InventoryPieceStatus.Available =>
                Error(StatusCodes.Status409Conflict, "PIECE_NOT_AVAILABLE"),
            _ when !matches[0].IsActive || matches[0].SaleMode != ProductSaleMode.Weight =>
                Error(StatusCodes.Status404NotFound, "WEIGHT_PRODUCT_NOT_AVAILABLE"),
            _ => Results.Ok(new PosPieceLookupResponse(matches[0].Id, matches[0].ProductId,
                matches[0].ProductCode, matches[0].ExternalIdentifier, matches[0].ReceivedWeightKg,
                matches[0].RawBarcode))
        };
    }

    private static async Task<IResult> GetAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var piece = await db.InventoryPieces.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == id && item.CompanyId == context.CompanyId && item.BranchId == context.BranchId,
            cancellationToken);
        return piece is null ? Error(StatusCodes.Status404NotFound, "PIECE_NOT_FOUND")
            : Results.Ok(ToResponse(piece));
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, string? status,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        InventoryPieceStatus? selectedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<InventoryPieceStatus>(status, false, out var parsed) ||
                !Enum.IsDefined(parsed))
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
            selectedStatus = parsed;
        }
        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 20;
        var context = accessor.Context;
        var query = db.InventoryPieces.AsNoTracking().Where(piece =>
            piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId);
        if (selectedStatus is not null)
            query = query.Where(piece => piece.Status == selectedStatus);
        var total = await query.CountAsync(cancellationToken);
        var pageItems = await (from piece in query
                           join product in db.CatalogProducts.AsNoTracking() on piece.ProductId equals product.Id
                           orderby piece.ReceivedAtUtc descending, piece.Id descending
                           select new { piece.Id, piece.ProductId, ProductCode = product.Code,
                               ProductName = product.Name,
                               piece.SourceSystem, piece.ExternalIdentifier, piece.ReceivedWeightKg,
                               piece.RawBarcode, piece.ReceivedAtUtc, piece.Status,
                               piece.ReservedBySaleDraftId, piece.ReservedAtUtc })
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToArrayAsync(cancellationToken);
        var items = pageItems.Select(piece => new PieceListItem(piece.Id, piece.ProductId,
            piece.ProductCode, piece.ProductName, piece.SourceSystem, piece.ExternalIdentifier,
            piece.ReceivedWeightKg, piece.RawBarcode, piece.ReceivedAtUtc,
            piece.Status.ToString(), piece.ReservedBySaleDraftId, piece.ReservedAtUtc)).ToArray();
        return Results.Ok(new PieceListResponse(items, currentPage, currentPageSize, total));
    }

    private static async Task<IResult> ListEventsAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        if (!await db.InventoryPieces.AsNoTracking().AnyAsync(piece => piece.Id == id &&
            piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PIECE_NOT_FOUND");
        var events = await db.InventoryPieceStateEvents.AsNoTracking()
            .Where(item => item.PieceId == id && item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId)
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Take(100).ToArrayAsync(cancellationToken);
        var items = events.Select(item => new PieceEventResponse(item.Id,
            item.PreviousStatus?.ToString(), item.Status.ToString(), item.Kind.ToString(),
            item.SaleDraftId, item.ActorUserId, item.Reason, item.CreatedAtUtc,
            item.EvidenceReference, item.EvidenceSha256, item.ObservedWeightKg,
            item.CountedOnHand, item.CountedAtUtc, item.InventoryMovementId)).ToArray();
        return Results.Ok(items);
    }

    private static async Task<IResult> ReleaseReservedAsync(Guid id, ReleasePieceRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext httpContext,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
            return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        var reason = request?.Reason?.Trim();
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 240)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var context = accessor.Context;
        var admin = await (from assignment in db.UserAssignments.AsNoTracking()
                           join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                           where assignment.UserId == context.UserId &&
                                 assignment.CompanyId == context.CompanyId &&
                                 assignment.BranchId == null && role.Code == "administrator"
                           select assignment.UserId).AnyAsync(cancellationToken);
        if (!admin) return Error(StatusCodes.Status403Forbidden, "ADMINISTRATOR_REQUIRED");

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var piece = await db.InventoryPieces.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == id && item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId, cancellationToken);
            if (piece is null) return Error(StatusCodes.Status404NotFound, "PIECE_NOT_FOUND");
            if (piece.Status != InventoryPieceStatus.Reserved ||
                piece.ReservedBySaleDraftId is not Guid draftId)
                return Error(StatusCodes.Status409Conflict, "PIECE_NOT_RESERVED");
            if (!await SaleDraftTransactionLock.AcquireAsync(db, context.CompanyId,
                    context.BranchId, draftId, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "PIECE_DRAFT_NOT_ACTIVE");
            var draft = await db.SaleDrafts.Include(item => item.Lines).SingleOrDefaultAsync(item =>
                item.Id == draftId && item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.Status == SaleDraftStatus.Draft,
                cancellationToken);
            if (draft is null) return Error(StatusCodes.Status409Conflict, "PIECE_DRAFT_NOT_ACTIVE");
            var line = draft.Lines.SingleOrDefault(item => item.InventoryPieceId == piece.Id);
            if (line is null) return Error(StatusCodes.Status409Conflict, "PIECE_DRAFT_LINE_MISSING");
            if (await db.ConfirmedSales.AsNoTracking().AnyAsync(sale =>
                    sale.CompanyId == context.CompanyId && sale.SourceDraftId == draftId,
                    cancellationToken))
                return Error(StatusCodes.Status409Conflict, "SALE_ALREADY_CONFIRMED");
            if (await db.PointPaymentIntents.AsNoTracking().AnyAsync(intent =>
                    intent.CompanyId == context.CompanyId && intent.SaleDraftId == draftId &&
                    (intent.Status == PointPaymentStatus.Prepared ||
                     intent.Status == PointPaymentStatus.Pending ||
                     intent.Status == PointPaymentStatus.Approved ||
                     intent.Status == PointPaymentStatus.NeedsReconciliation), cancellationToken))
                return Error(StatusCodes.Status409Conflict, "MERCADO_PAGO_ORDER_ACTIVE");

            var now = clock.GetUtcNow();
            if (await StockReconciliationLease.IsLockedForUpdateAsync(db, context.CompanyId,
                    context.BranchId, piece.ProductId, now, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "ARTICLE_RECONCILIATION_IN_PROGRESS");
            if (!await PieceReservationTransactions.ReleaseAsync(db, piece, draftId,
                context.UserId, InventoryPieceEventKind.AdminReleased, reason,
                now, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "PIECE_RESERVATION_MISSING");
            var changed = await StockReconciliationLease.Unlocked(db.BranchInventoryBalances.Where(balance =>
                    balance.CompanyId == context.CompanyId && balance.BranchId == context.BranchId &&
                    balance.ProductId == piece.ProductId && balance.Reserved >= line.Quantity), now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(balance => balance.Reserved,
                    balance => balance.Reserved - line.Quantity), cancellationToken);
            if (changed != 1) return Error(StatusCodes.Status409Conflict, "STOCK_RESERVATION_MISSING");

            draft.ReplaceLines(draft.Lines.Where(item => item.Id != line.Id).ToArray(), now);
            var discountReset = draft.DiscountAmount > 0 && (draft.Lines.Count == 0 ||
                draft.DiscountAmount >= draft.Lines.Sum(item => decimal.Round(
                    item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero)));
            if (discountReset) draft.SetDiscount(0, null);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { pieceId = piece.Id, draftId, draftCancelled = false,
                discountReset });
        }
        catch (Exception exception) when (PieceReservationTransactions.IsConcurrencyConflict(exception))
        {
            return Error(StatusCodes.Status409Conflict, "PIECE_OPERATION_IN_PROGRESS");
        }
    }

    private static async Task<IResult> ReceiveAsync(ReceivePieceRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext httpContext,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
            return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || request.OperationId == Guid.Empty || request.ProductId == Guid.Empty ||
            request.BarcodeProfileId == Guid.Empty || string.IsNullOrWhiteSpace(request.SourceSystem) ||
            request.SourceSystem.Trim().Length > 120 || string.IsNullOrWhiteSpace(request.IdentifierField) ||
            request.IdentifierField.Trim().Length > 80 || string.IsNullOrWhiteSpace(request.Code) ||
            request.Code.Trim().Length > 80)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = accessor.Context;
        var source = request.SourceSystem.Trim().Normalize(NormalizationForm.FormKC);
        if (source.Length > 120)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var normalizedSource = source.ToUpperInvariant();
        var identifierField = request.IdentifierField.Trim().ToLowerInvariant();
        var code = request.Code.Trim();
        var existing = await db.InventoryPieces.AsNoTracking().SingleOrDefaultAsync(piece =>
            piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId &&
            piece.OperationId == request.OperationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ProductId != request.ProductId || existing.BarcodeProfileId != request.BarcodeProfileId ||
                existing.NormalizedSourceSystem != normalizedSource || existing.IdentifierField != identifierField ||
                existing.RawBarcode != code)
                return Error(StatusCodes.Status409Conflict, "OPERATION_ID_REUSED");
            return Results.Ok(ToResponse(existing));
        }

        var profile = await db.BarcodeProfiles.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.Id == request.BarcodeProfileId, cancellationToken);
        if (profile is null) return Error(StatusCodes.Status404NotFound, "BARCODE_PROFILE_NOT_AVAILABLE");
        var product = await db.CatalogProducts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.Id == request.ProductId && item.IsActive,
            cancellationToken);
        if (product is null || product.SaleMode != ProductSaleMode.Weight)
            return Error(StatusCodes.Status404NotFound, "WEIGHT_PRODUCT_NOT_AVAILABLE");

        PieceBarcodeRead read;
        try
        {
            read = PieceBarcodeRead.Parse(profile.Formula, identifierField,
                profile.WeightField, profile.WeightDecimals, code);
        }
        catch (KeyNotFoundException)
        {
            return Error(StatusCodes.Status400BadRequest, "IDENTIFIER_FIELD_NOT_IN_PROFILE");
        }
        catch (FormatException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_BARCODE");
        }
        catch (ArgumentOutOfRangeException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_PIECE_WEIGHT_OR_IDENTIFIER");
        }
        catch (ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "IDENTIFIER_FIELD_NOT_IN_PROFILE");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var movement = new InventoryMovement(context.CompanyId, context.BranchId, product.Id,
            context.UserId, request.OperationId, InventoryMovementKind.PieceReceipt, read.WeightKg,
            "Recepción de pieza trazable", now);
        var piece = new InventoryPiece(context.CompanyId, context.BranchId, product.Id,
            profile.Id, context.UserId, request.OperationId, movement.Id, source,
            read.ExternalIdentifier, code, read.WeightKg, now, identifierField);
        var receipt = InventoryReceipt.LegacyPiece(context.CompanyId, context.BranchId,
            request.OperationId, source, product, read.WeightKg, context.UserId, now);
        receipt.LinkMovement(receipt.Lines[0].Id, movement.Id);
        db.InventoryMovements.Add(movement);
        db.InventoryPieces.Add(piece);
        db.InventoryReceipts.Add(receipt);
        db.InventoryPieceStateEvents.Add(new InventoryPieceStateEvent(context.CompanyId,
            context.BranchId, piece.Id, null, InventoryPieceStatus.Available,
            InventoryPieceEventKind.Receipt, null, context.UserId,
            "Recepción de pieza trazable", now));
        try
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inventory.branch_inventory ("CompanyId", "BranchId", "ProductId", "OnHand", "Reserved")
                VALUES ({context.CompanyId}, {context.BranchId}, {product.Id}, {read.WeightKg}, 0)
                ON CONFLICT ("CompanyId", "BranchId", "ProductId") DO UPDATE
                    SET "OnHand" = inventory.branch_inventory."OnHand" + EXCLUDED."OnHand"
                WHERE inventory.branch_inventory."ReconciliationLockExpiresAtUtc" IS NULL OR
                    inventory.branch_inventory."ReconciliationLockExpiresAtUtc" <= {now}
                """, cancellationToken);
            if (changed != 1)
                return Error(StatusCodes.Status409Conflict, "ARTICLE_RECONCILIATION_IN_PROGRESS");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            return postgres.ConstraintName == "IX_pieces_CompanyId_NormalizedSourceSystem_ExternalIdentifier"
                ? Error(StatusCodes.Status409Conflict, "PIECE_ALREADY_RECEIVED")
                : Error(StatusCodes.Status409Conflict, "OPERATION_ID_REUSED");
        }

        return Results.Created($"/api/inventory/pieces/{piece.Id}", ToResponse(piece));
    }

    private static PieceResponse ToResponse(InventoryPiece piece) => new(piece.Id, piece.OperationId,
        piece.ProductId, piece.BarcodeProfileId, piece.SourceSystem, piece.ExternalIdentifier,
        piece.ReceivedWeightKg, piece.RawBarcode, piece.ReceivedAtUtc, piece.Status.ToString());

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo procesar la pieza", [])), statusCode: statusCode);

    private sealed record ReceivePieceRequest(Guid OperationId, Guid ProductId, Guid BarcodeProfileId,
        string SourceSystem, string IdentifierField, string Code);
    private sealed record PieceResponse(Guid Id, Guid OperationId, Guid ProductId, Guid BarcodeProfileId,
        string SourceSystem, string ExternalIdentifier, decimal ReceivedWeightKg, string RawBarcode,
        DateTimeOffset ReceivedAtUtc, string Status);
    private sealed record PieceListItem(Guid Id, Guid ProductId, string ProductCode, string ProductName,
        string SourceSystem, string ExternalIdentifier, decimal ReceivedWeightKg, string RawBarcode,
        DateTimeOffset ReceivedAtUtc, string Status, Guid? ReservedBySaleDraftId,
        DateTimeOffset? ReservedAtUtc);
    private sealed record PieceListResponse(IReadOnlyList<PieceListItem> Items, int Page, int PageSize, int Total);
    private sealed record PieceEventResponse(Guid Id, string? PreviousStatus, string Status,
        string Kind, Guid? SaleDraftId, Guid? ActorUserId, string Reason, DateTimeOffset CreatedAtUtc,
        string? EvidenceReference, string? EvidenceSha256, decimal? ObservedWeightKg,
        decimal? CountedOnHand, DateTimeOffset? CountedAtUtc, Guid? InventoryMovementId);
    private sealed record ReleasePieceRequest(string? Reason);
    private sealed record PosPieceLookupResponse(Guid Id, Guid ProductId, string ProductCode,
        string ExternalIdentifier, decimal ReceivedWeightKg, string RawBarcode);
}
