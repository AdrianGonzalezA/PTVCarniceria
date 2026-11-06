using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Inventory;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class InventoryTransferReceiptEndpoints
{
    public static IEndpointRouteBuilder MapInventoryTransferReceiptEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var receipts = endpoints.MapGroup("/api/inventory/transfer-receipts")
            .RequireOperationalPermission(PlatformPermissionCatalog.InventoryReceiptsManage);
        receipts.MapGet("", ListAsync);
        receipts.MapGet("/{id:guid}", GetAsync);
        receipts.MapPost("", CreateAsync);
        receipts.MapPost("/import", ImportAsync);
        receipts.MapPost("/{id:guid}/lines", AddLineAsync);
        receipts.MapPatch("/{id:guid}/lines/{lineId:guid}", SetReceivedAsync);
        receipts.MapDelete("/{id:guid}/lines/{lineId:guid}", RemoveLineAsync);
        receipts.MapPost("/{id:guid}/lines/{lineId:guid}/scan", ScanPieceAsync);
        receipts.MapPost("/{id:guid}/approve", ApproveAsync);
        receipts.MapPost("/{id:guid}/cancel", CancelAsync);
        receipts.MapPost("/{id:guid}/lines/{lineId:guid}/resolve", ResolveDifferenceAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 20, string? status = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue ||
            status is not (null or "Draft" or "Approved" or "Canceled"))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var query = db.InventoryTransferReceipts.AsNoTracking().Where(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId);
        if (status is not null)
        {
            var value = Enum.Parse<InventoryTransferReceiptStatus>(status);
            query = query.Where(item => item.Status == value);
        }
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new ReceiptSummary(item.Id, item.SourceBranchName,
                item.DocumentNumber, item.DocumentDate, item.Status.ToString(),
                item.Version, item.CreatedAtUtc, item.ApprovedAtUtc,
                item.ImportedSha256 != null))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new ReceiptPage(items, page, pageSize, total,
            total == 0 ? 0 : (total - 1) / pageSize + 1));
    }

    private static async Task<IResult> GetAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var receipt = await FindAsync(db, accessor, id, false, cancellationToken);
        return receipt is null ? Error(404, "TRANSFER_RECEIPT_NOT_FOUND") :
            Results.Ok(ToResponse(receipt));
    }

    private static async Task<IResult> CreateAsync(CreateRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.OperationId == Guid.Empty ||
            request.DocumentDate == default || !Required(request.SourceBranchName, 200) ||
            !Required(request.DocumentNumber, 80) ||
            request.SourceBranchId == Guid.Empty || request.SourceTransferId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (request.SourceBranchId == context.BranchId)
            return Error(400, "ORIGIN_IS_DESTINATION");
        var existing = await db.InventoryTransferReceipts.Include(item => item.Lines)
            .AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.OperationId == request.OperationId,
                cancellationToken);
        if (existing is not null)
            return existing.SourceBranchName == request.SourceBranchName.Trim() &&
                   existing.DocumentNumber == request.DocumentNumber.Trim() &&
                   existing.DocumentDate == request.DocumentDate &&
                   existing.SourceBranchId == request.SourceBranchId &&
                   existing.SourceTransferId == request.SourceTransferId &&
                   existing.ImportedSha256 is null
                ? Results.Ok(ToResponse(existing)) : Error(409, "OPERATION_ID_REUSED");
        var companyCode = await db.Companies.AsNoTracking().Where(item =>
            item.Id == context.CompanyId).Select(item => item.Code)
            .SingleAsync(cancellationToken);
        var receipt = new InventoryTransferReceipt(context.CompanyId, context.BranchId,
            request.OperationId, companyCode, request.SourceBranchName,
            request.DocumentNumber, request.DocumentDate, context.UserId,
            clock.GetUtcNow(), request.SourceBranchId, request.SourceTransferId);
        db.InventoryTransferReceipts.Add(receipt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/inventory/transfer-receipts/{receipt.Id}",
                ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "REMITO_ALREADY_REGISTERED");
        }
    }

    private static async Task<IResult> ImportAsync(ImportRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.OperationId == Guid.Empty ||
            request.Remito.ValueKind != JsonValueKind.Object)
            return Error(400, "VALIDATION_ERROR");
        var raw = request.Remito.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > 1_000_000)
            return Error(413, "REMITO_TOO_LARGE");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
            .ToLowerInvariant();
        var context = accessor.Context;
        var existing = await db.InventoryTransferReceipts.Include(item => item.Lines)
            .AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.OperationId == request.OperationId,
                cancellationToken);
        if (existing is not null)
            return existing.ImportedSha256 == hash ? Results.Ok(ToResponse(existing)) :
                Error(409, "OPERATION_ID_REUSED");
        var company = await db.Companies.AsNoTracking().SingleAsync(item =>
            item.Id == context.CompanyId, cancellationToken);
        var branch = await db.Branches.AsNoTracking().SingleAsync(item =>
            item.CompanyId == context.CompanyId && item.Id == context.BranchId,
            cancellationToken);
        var products = await db.CatalogProducts.AsNoTracking().Where(item =>
            item.CompanyId == context.CompanyId && item.IsActive)
            .ToArrayAsync(cancellationToken);
        var plan = TransferRemitoImportPlanner.Plan(request.Remito, context.CompanyId,
            context.BranchId, company.Code, branch.Name, products, request.OperationId,
            context.UserId, clock.GetUtcNow());
        if (plan.Error is not null) return Error(400, plan.Error);
        var receipt = plan.Receipt!;
        var localSource = await db.InventoryTransfers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                item.Id == receipt.SourceTransferId, cancellationToken);
        if (localSource is not null &&
            (localSource.Status != InventoryTransferStatus.Dispatched ||
             localSource.DocumentNumber != receipt.DocumentNumber ||
             localSource.BranchId != receipt.SourceBranchId ||
             localSource.DestinationBranchId != context.BranchId &&
             !string.Equals(localSource.DestinationName, branch.Name,
                 StringComparison.OrdinalIgnoreCase)))
            return Error(409, "LOCAL_DISPATCH_MISMATCH");
        db.InventoryTransferReceipts.Add(receipt);
        db.InventoryTransferReceiptLines.AddRange(plan.Lines);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/inventory/transfer-receipts/{receipt.Id}",
                ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "REMITO_ALREADY_REGISTERED");
        }
    }

    private static async Task<IResult> AddLineAsync(Guid id, AddLineRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1 || request.ProductId == Guid.Empty ||
            request.SourceLineId == Guid.Empty || request.SourcePieceId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "TRANSFER_RECEIPT_NOT_FOUND");
            if (receipt.Status != InventoryTransferReceiptStatus.Draft)
                return Error(409, "TRANSFER_RECEIPT_NOT_DRAFT");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            if (receipt.Lines.Count >= 500) return Error(400, "RECEIPT_LINE_LIMIT");
            var product = await db.CatalogProducts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == accessor.Context.CompanyId &&
                item.Id == request.ProductId && item.IsActive, cancellationToken);
            if (product is null) return Error(404, "PRODUCT_NOT_AVAILABLE");
            InventoryTransferReceiptLine line;
            try
            {
                line = new InventoryTransferReceiptLine(receipt.Id, product,
                    request.SentQuantity, request.ReceivedQuantity, request.IsPiece,
                    request.SourceLineId, request.SourcePieceId, request.SourceSystem,
                    request.SourceExternalIdentifier, request.SourceRawBarcode);
                receipt.AddLine(line);
            }
            catch (Exception exception) when (exception is ArgumentException or
                InvalidOperationException) { return Error(400, "INVALID_TRANSFER_LINE"); }
            db.InventoryTransferReceiptLines.Add(line);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_VERSION_CHANGED");
        }
    }

    private static async Task<IResult> SetReceivedAsync(Guid id, Guid lineId,
        SetReceivedRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1) return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "TRANSFER_RECEIPT_NOT_FOUND");
            var line = receipt.Lines.SingleOrDefault(item => item.Id == lineId);
            if (line is null) return Error(404, "TRANSFER_RECEIPT_LINE_NOT_FOUND");
            if (receipt.Status != InventoryTransferReceiptStatus.Draft)
                return Error(409, "TRANSFER_RECEIPT_NOT_DRAFT");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            try { receipt.SetGenericReceived(lineId, request.ReceivedQuantity); }
            catch (InvalidOperationException) { return Error(400, "INVALID_RECEIVED_QUANTITY"); }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_VERSION_CHANGED");
        }
    }

    private static async Task<IResult> RemoveLineAsync(Guid id, Guid lineId,
        int version, PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (version < 1) return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "TRANSFER_RECEIPT_NOT_FOUND");
            var line = receipt.Lines.SingleOrDefault(item => item.Id == lineId);
            if (line is null) return Error(404, "TRANSFER_RECEIPT_LINE_NOT_FOUND");
            if (receipt.Status != InventoryTransferReceiptStatus.Draft)
                return Error(409, "TRANSFER_RECEIPT_NOT_DRAFT");
            if (receipt.Version != version) return Error(409, "RECEIPT_VERSION_CHANGED");
            receipt.RemoveLine(lineId);
            db.InventoryTransferReceiptLines.Remove(line);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_VERSION_CHANGED");
        }
    }

    private static async Task<IResult> ScanPieceAsync(Guid id, Guid lineId,
        ScanPieceRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1 || request.BarcodeProfileId == Guid.Empty ||
            !Required(request.SourceSystem, 120) || !Required(request.IdentifierField, 80) ||
            !Required(request.Code, 80))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var profile = await db.BarcodeProfiles.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.Id == request.BarcodeProfileId,
            cancellationToken);
        if (profile is null) return Error(404, "BARCODE_PROFILE_NOT_AVAILABLE");
        PieceBarcodeRead parsed;
        try
        {
            parsed = PieceBarcodeRead.Parse(profile.Formula,
                request.IdentifierField.Trim().ToLowerInvariant(),
                profile.WeightField, profile.WeightDecimals, request.Code.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or
            KeyNotFoundException) { return Error(400, "INVALID_BARCODE"); }
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "TRANSFER_RECEIPT_NOT_FOUND");
            var line = receipt.Lines.SingleOrDefault(item => item.Id == lineId);
            if (line is null) return Error(404, "TRANSFER_RECEIPT_LINE_NOT_FOUND");
            if (line.HasPhysicalRead)
                return line.BarcodeProfileId == profile.Id &&
                       string.Equals(line.ReadSourceSystem, request.SourceSystem.Trim(),
                           StringComparison.OrdinalIgnoreCase) &&
                       line.RawBarcode == request.Code.Trim()
                    ? Results.Ok(ToResponse(receipt)) : Error(409, "PIECE_ALREADY_SCANNED");
            if (receipt.Status != InventoryTransferReceiptStatus.Draft || !line.IsPiece)
                return Error(409, "TRANSFER_RECEIPT_LINE_NOT_SCANNABLE");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            var source = request.SourceSystem.Trim();
            var normalizedSource = source.ToUpperInvariant();
            var identifier = parsed.ExternalIdentifier;
            if (receipt.Lines.Any(item => item.Id != lineId && item.IsPiece &&
                    string.Equals(item.ReadSourceSystem, source,
                        StringComparison.OrdinalIgnoreCase) &&
                    item.ExternalIdentifier == identifier) ||
                await db.InventoryPieces.AsNoTracking().AnyAsync(item =>
                    item.CompanyId == context.CompanyId &&
                    item.NormalizedSourceSystem == normalizedSource &&
                    item.ExternalIdentifier == identifier &&
                    item.Status != InventoryPieceStatus.Transferred, cancellationToken))
                return Error(409, "PIECE_ALREADY_AVAILABLE");
            receipt.CapturePiece(lineId, profile.Id, source,
                request.IdentifierField.Trim(), identifier, request.Code.Trim(),
                parsed.WeightKg);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_VERSION_CHANGED");
        }
    }

    private static async Task<IResult> ApproveAsync(Guid id, VersionRequest? request,
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
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "TRANSFER_RECEIPT_NOT_FOUND");
            if (receipt.Status == InventoryTransferReceiptStatus.Approved)
                return Results.Ok(ToResponse(receipt));
            if (receipt.Status != InventoryTransferReceiptStatus.Draft)
                return Error(409, "TRANSFER_RECEIPT_NOT_DRAFT");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            if (receipt.Lines.Count == 0 || receipt.Lines.Any(line =>
                    line.SentQuantity == 0 && line.ReceivedQuantity == 0 ||
                    line.IsPiece && line.ReceivedQuantity > 0 && !line.HasPhysicalRead))
                return Error(400, "PHYSICAL_RECEIPT_INCOMPLETE");
            var positive = receipt.Lines.Where(line => line.ReceivedQuantity > 0).ToArray();
            var productIds = positive.Select(line => line.ProductId).Distinct().ToArray();
            var products = await db.CatalogProducts.AsNoTracking().Where(product =>
                    product.CompanyId == context.CompanyId && productIds.Contains(product.Id) &&
                    product.IsActive).ToDictionaryAsync(product => product.Id, cancellationToken);
            if (products.Count != productIds.Length || positive.Any(line =>
                    products[line.ProductId].SaleMode != line.SaleMode ||
                    products[line.ProductId].Unit != line.Unit))
                return Error(409, "PRODUCT_NOT_AVAILABLE");
            var reads = positive.Where(line => line.IsPiece).ToArray();
            if (reads.GroupBy(line => new { Source = line.ReadSourceSystem!.ToUpperInvariant(),
                    line.ExternalIdentifier }).Any(group => group.Count() > 1))
                return Error(409, "PIECE_DUPLICATED_IN_REMITO");
            var sources = reads.Select(line => line.ReadSourceSystem!.ToUpperInvariant())
                .Distinct().ToArray();
            var identifiers = reads.Select(line => line.ExternalIdentifier!).Distinct().ToArray();
            var activeIdentities = await db.InventoryPieces.AsNoTracking().Where(piece =>
                    piece.CompanyId == context.CompanyId &&
                    sources.Contains(piece.NormalizedSourceSystem) &&
                    identifiers.Contains(piece.ExternalIdentifier) &&
                    piece.Status != InventoryPieceStatus.Transferred)
                .Select(piece => new { piece.NormalizedSourceSystem, piece.ExternalIdentifier })
                .ToArrayAsync(cancellationToken);
            if (activeIdentities.Any(piece => reads.Any(line =>
                    string.Equals(line.ReadSourceSystem, piece.NormalizedSourceSystem,
                        StringComparison.OrdinalIgnoreCase) &&
                    line.ExternalIdentifier == piece.ExternalIdentifier)))
                return Error(409, "PIECE_ALREADY_AVAILABLE");

            var localDispatch = receipt.SourceTransferId.HasValue
                ? await db.InventoryTransfers.Include(item => item.Lines).AsNoTracking()
                    .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                        item.Id == receipt.SourceTransferId, cancellationToken)
                : null;
            if (localDispatch is not null)
            {
                if (localDispatch.Status != InventoryTransferStatus.Dispatched ||
                    localDispatch.BranchId != receipt.SourceBranchId ||
                    localDispatch.DocumentNumber != receipt.DocumentNumber ||
                    localDispatch.DestinationBranchId != context.BranchId &&
                    !string.Equals(localDispatch.DestinationName,
                        (await db.Branches.AsNoTracking().Where(branch =>
                            branch.Id == context.BranchId).Select(branch => branch.Name)
                            .SingleAsync(cancellationToken)), StringComparison.OrdinalIgnoreCase))
                    return Error(409, "LOCAL_DISPATCH_MISMATCH");
                foreach (var line in receipt.Lines.Where(line => line.SourceLineId.HasValue))
                {
                    var sent = localDispatch.Lines.SingleOrDefault(item =>
                        item.Id == line.SourceLineId);
                    if (sent is null || sent.ProductId != line.ProductId ||
                        sent.Quantity != line.SentQuantity || sent.PieceId != line.SourcePieceId)
                        return Error(409, "LOCAL_DISPATCH_LINE_MISMATCH");
                }
                foreach (var line in reads.Where(line => line.SourcePieceId.HasValue))
                {
                    var sourcePiece = await db.InventoryPieces.AsNoTracking().SingleOrDefaultAsync(
                        piece => piece.CompanyId == context.CompanyId &&
                            piece.Id == line.SourcePieceId &&
                            piece.BranchId == localDispatch.BranchId &&
                            piece.Status == InventoryPieceStatus.Transferred &&
                            piece.ProductId == line.ProductId, cancellationToken);
                    if (sourcePiece is null || sourcePiece.ExternalIdentifier != line.ExternalIdentifier)
                        return Error(409, "SOURCE_PIECE_MISMATCH");
                    line.MarkSourcePieceVerified();
                }
            }
            var now = clock.GetUtcNow();
            foreach (var group in positive.GroupBy(line => line.ProductId).OrderBy(group => group.Key))
            {
                if (await StockReconciliationLease.IsLockedForUpdateAsync(db, context.CompanyId,
                        context.BranchId, group.Key, now, cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
                var quantity = group.Sum(line => line.ReceivedQuantity);
                var protectedQuantity = group.Where(line => line.IsPiece)
                    .Sum(line => line.ReceivedQuantity);
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO inventory.branch_inventory
                        ("CompanyId", "BranchId", "ProductId", "OnHand", "Reserved", "ProtectedOnHand", "ProtectedReserved")
                    VALUES ({context.CompanyId}, {context.BranchId}, {group.Key}, {quantity}, 0, {protectedQuantity}, 0)
                    ON CONFLICT ("CompanyId", "BranchId", "ProductId") DO UPDATE
                        SET "OnHand" = inventory.branch_inventory."OnHand" + EXCLUDED."OnHand",
                            "ProtectedOnHand" = inventory.branch_inventory."ProtectedOnHand" + EXCLUDED."ProtectedOnHand"
                    WHERE inventory.branch_inventory."ReconciliationLockExpiresAtUtc" IS NULL OR
                        inventory.branch_inventory."ReconciliationLockExpiresAtUtc" <= {now}
                    """, cancellationToken);
                if (changed != 1) return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
            }
            receipt.Approve(context.UserId, now);
            foreach (var line in positive)
            {
                var movement = new InventoryMovement(context.CompanyId, context.BranchId,
                    line.ProductId, context.UserId, line.Id,
                    InventoryMovementKind.TransferIn, line.ReceivedQuantity,
                    $"Transferencia recibida {receipt.DocumentNumber}", now);
                db.InventoryMovements.Add(movement);
                if (line.IsPiece)
                {
                    var piece = new InventoryPiece(context.CompanyId, context.BranchId,
                        line.ProductId, line.BarcodeProfileId!.Value, context.UserId,
                        line.Id, movement.Id, line.ReadSourceSystem!, line.ExternalIdentifier!,
                        line.RawBarcode!, line.ReceivedQuantity, now, line.IdentifierField!,
                        line.SourcePieceVerified ? line.SourcePieceId : null);
                    db.InventoryPieces.Add(piece);
                    receipt.LinkMovement(line.Id, movement.Id, piece.Id);
                    db.InventoryPieceStateEvents.Add(new InventoryPieceStateEvent(
                        context.CompanyId, context.BranchId, piece.Id, null,
                        InventoryPieceStatus.Available, InventoryPieceEventKind.Receipt,
                        null, context.UserId, "Recepción de transferencia trazable", now));
                }
                else receipt.LinkMovement(line.Id, movement.Id);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "TRANSFER_RECEIPT_CONFLICT");
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
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "TRANSFER_RECEIPT_NOT_FOUND");
            if (receipt.Status == InventoryTransferReceiptStatus.Canceled)
                return Results.Ok(ToResponse(receipt));
            if (receipt.Status != InventoryTransferReceiptStatus.Draft)
                return Error(409, "TRANSFER_RECEIPT_NOT_DRAFT");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            receipt.Cancel(accessor.Context.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "TRANSFER_RECEIPT_CONFLICT");
        }
    }

    private static async Task<IResult> ResolveDifferenceAsync(Guid id, Guid lineId,
        ResolveDifferenceRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext http, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1 || !Required(request.Reason, 240))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var isAdministrator = await db.UserAssignments.AsNoTracking().AnyAsync(assignment =>
            assignment.CompanyId == context.CompanyId && assignment.UserId == context.UserId &&
            assignment.BranchId == null && db.Roles.Any(role =>
                role.Id == assignment.RoleId && role.Code == "administrator"), cancellationToken);
        if (!isAdministrator) return Error(403, "ADMINISTRATOR_REQUIRED");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "TRANSFER_RECEIPT_NOT_FOUND");
            var line = receipt.Lines.SingleOrDefault(item => item.Id == lineId);
            if (line is null) return Error(404, "TRANSFER_RECEIPT_LINE_NOT_FOUND");
            if (line.DifferenceStatus == InventoryTransferDifferenceStatus.Resolved)
                return line.DifferenceReason == request.Reason.Trim()
                    ? Results.Ok(ToResponse(receipt)) : Error(409, "DIFFERENCE_ALREADY_RESOLVED");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            if (line.DifferenceStatus != InventoryTransferDifferenceStatus.PendingReview)
                return Error(409, "DIFFERENCE_NOT_PENDING");
            receipt.ResolveDifference(lineId, context.UserId, request.Reason,
                clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "TRANSFER_RECEIPT_CONFLICT");
        }
    }

    private static Task<InventoryTransferReceipt?> FindAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, Guid id, bool tracked,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var query = db.InventoryTransferReceipts.Include(item => item.Lines).AsQueryable();
        if (!tracked) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(item => item.Id == id &&
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId,
            cancellationToken);
    }

    private static ReceiptResponse ToResponse(InventoryTransferReceipt receipt) => new(
        receipt.Id, receipt.OperationId, receipt.BranchId, receipt.SourceBranchName,
        receipt.SourceBranchId, receipt.SourceTransferId, receipt.DocumentNumber,
        receipt.DocumentDate, receipt.Status.ToString(), receipt.Version,
        receipt.ImportedSha256, receipt.CreatedAtUtc, receipt.ApprovedAtUtc,
        receipt.CanceledAtUtc, receipt.Lines.Select(line => new LineResponse(
            line.Id, line.ProductId, line.ProductCode, line.ProductName,
            line.Unit, line.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
            line.SentQuantity, line.ReceivedQuantity, line.IsPiece,
            line.SourceLineId, line.SourcePieceId, line.SourceSystem,
            line.SourceExternalIdentifier, line.SourceRawBarcode,
            line.BarcodeProfileId, line.ReadSourceSystem, line.IdentifierField,
            line.ExternalIdentifier, line.RawBarcode, line.SourcePieceVerified,
            line.InventoryMovementId, line.InventoryPieceId,
            line.DifferenceStatus.ToString(), line.DifferenceReason,
            line.DifferenceResolvedByUserId, line.DifferenceResolvedAtUtc)).ToArray());

    private static bool Required(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= limit;

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin,
            context.Request);
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
        new ErrorResponse(new ApiError(code,
            "No se pudo procesar la recepción de transferencia", [])),
        statusCode: status);

    private sealed record CreateRequest(Guid OperationId, string SourceBranchName,
        string DocumentNumber, DateOnly DocumentDate, Guid? SourceBranchId,
        Guid? SourceTransferId);
    private sealed record ImportRequest(Guid OperationId, JsonElement Remito);
    private sealed record AddLineRequest(int Version, Guid ProductId,
        decimal SentQuantity, decimal ReceivedQuantity, bool IsPiece,
        Guid? SourceLineId, Guid? SourcePieceId, string? SourceSystem,
        string? SourceExternalIdentifier, string? SourceRawBarcode);
    private sealed record SetReceivedRequest(int Version, decimal ReceivedQuantity);
    private sealed record ScanPieceRequest(int Version, Guid BarcodeProfileId,
        string SourceSystem, string IdentifierField, string Code);
    private sealed record VersionRequest(int Version);
    private sealed record ResolveDifferenceRequest(int Version, string Reason);
    private sealed record ReceiptSummary(Guid Id, string SourceBranchName,
        string DocumentNumber, DateOnly DocumentDate, string Status, int Version,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? ApprovedAtUtc,
        bool IsImported);
    private sealed record ReceiptPage(IReadOnlyList<ReceiptSummary> Items, int Page,
        int PageSize, long TotalItems, long TotalPages);
    private sealed record LineResponse(Guid Id, Guid ProductId, string ProductCode,
        string ProductName, string Unit, string SaleMode, decimal SentQuantity,
        decimal ReceivedQuantity, bool IsPiece, Guid? SourceLineId,
        Guid? SourcePieceId, string? SourceSystem,
        string? SourceExternalIdentifier, string? SourceRawBarcode,
        Guid? BarcodeProfileId, string? ReadSourceSystem, string? IdentifierField,
        string? ExternalIdentifier, string? RawBarcode, bool SourcePieceVerified,
        Guid? InventoryMovementId, Guid? InventoryPieceId, string DifferenceStatus,
        string? DifferenceReason, Guid? DifferenceResolvedByUserId,
        DateTimeOffset? DifferenceResolvedAtUtc);
    private sealed record ReceiptResponse(Guid Id, Guid OperationId, Guid BranchId,
        string SourceBranchName, Guid? SourceBranchId, Guid? SourceTransferId,
        string DocumentNumber, DateOnly DocumentDate, string Status, int Version,
        string? ImportedSha256, DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ApprovedAtUtc, DateTimeOffset? CanceledAtUtc,
        IReadOnlyList<LineResponse> Lines);
}
