using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
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
        pieces.MapPost("", ReceiveAsync);
        return endpoints;
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

    private static async Task<IResult> ListAsync(int? page, int? pageSize,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 20;
        var context = accessor.Context;
        var query = db.InventoryPieces.AsNoTracking().Where(piece =>
            piece.CompanyId == context.CompanyId && piece.BranchId == context.BranchId);
        var total = await query.CountAsync(cancellationToken);
        var items = await (from piece in query
                           join product in db.CatalogProducts.AsNoTracking() on piece.ProductId equals product.Id
                           orderby piece.ReceivedAtUtc descending, piece.Id descending
                           select new PieceListItem(piece.Id, piece.ProductId, product.Code, product.Name,
                               piece.SourceSystem, piece.ExternalIdentifier, piece.ReceivedWeightKg,
                               piece.RawBarcode, piece.ReceivedAtUtc))
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new PieceListResponse(items, currentPage, currentPageSize, total));
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
        var movement = new InventoryMovement(context.CompanyId, context.BranchId, product.Id,
            context.UserId, request.OperationId, InventoryMovementKind.PieceReceipt, read.WeightKg,
            "Recepción de pieza trazable", clock.GetUtcNow());
        var piece = new InventoryPiece(context.CompanyId, context.BranchId, product.Id,
            profile.Id, context.UserId, request.OperationId, movement.Id, source,
            read.ExternalIdentifier, code, read.WeightKg, clock.GetUtcNow(), identifierField);
        db.InventoryMovements.Add(movement);
        db.InventoryPieces.Add(piece);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inventory.branch_inventory ("CompanyId", "BranchId", "ProductId", "OnHand", "Reserved")
                VALUES ({context.CompanyId}, {context.BranchId}, {product.Id}, {read.WeightKg}, 0)
                ON CONFLICT ("CompanyId", "BranchId", "ProductId") DO UPDATE
                    SET "OnHand" = inventory.branch_inventory."OnHand" + EXCLUDED."OnHand"
                """, cancellationToken);
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
        piece.ReceivedWeightKg, piece.RawBarcode, piece.ReceivedAtUtc);

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo recibir la pieza", [])), statusCode: statusCode);

    private sealed record ReceivePieceRequest(Guid OperationId, Guid ProductId, Guid BarcodeProfileId,
        string SourceSystem, string IdentifierField, string Code);
    private sealed record PieceResponse(Guid Id, Guid OperationId, Guid ProductId, Guid BarcodeProfileId,
        string SourceSystem, string ExternalIdentifier, decimal ReceivedWeightKg, string RawBarcode,
        DateTimeOffset ReceivedAtUtc);
    private sealed record PieceListItem(Guid Id, Guid ProductId, string ProductCode, string ProductName,
        string SourceSystem, string ExternalIdentifier, decimal ReceivedWeightKg, string RawBarcode,
        DateTimeOffset ReceivedAtUtc);
    private sealed record PieceListResponse(IReadOnlyList<PieceListItem> Items, int Page, int PageSize, int Total);
}
