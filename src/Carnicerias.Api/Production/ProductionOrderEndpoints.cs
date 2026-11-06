using System.Data;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Production;

public static partial class ProductionOrderEndpoints
{
    public static IEndpointRouteBuilder MapProductionOrderEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var orders = endpoints.MapGroup("/api/production/orders")
            .RequireOperationalPermission(PlatformPermissionCatalog.ProductionOperate);
        orders.MapGet("", ListAsync);
        orders.MapGet("/options", ListOptionsAsync);
        orders.MapGet("/pieces", ListPiecesAsync);
        orders.MapGet("/summary", SummaryAsync);
        orders.MapGet("/{id:guid}", GetAsync);
        orders.MapPost("", CreateAsync);
        orders.MapPut("/{id:guid}/lines", ReplaceLinesAsync);
        orders.MapPost("/{id:guid}/cancel", CancelAsync);
        orders.MapPost("/{id:guid}/close", CloseAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 20, string? status = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue ||
            status is not null && (!Enum.TryParse<ProductionOrderStatus>(
                status, false, out var parsed) || !Enum.IsDefined(parsed)))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var query = db.ProductionOrders.AsNoTracking().Where(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId);
        if (status is not null)
        {
            var state = Enum.Parse<ProductionOrderStatus>(status);
            query = query.Where(item => item.Status == state);
        }
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.Include(item => item.Lines)
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new OrderPage(rows.Select(ToResponse).ToArray(), page,
            pageSize, total, total == 0 ? 0 : (total - 1) / pageSize + 1));
    }

    private static async Task<IResult> GetAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var order = await FindAsync(db, accessor, id, cancellationToken);
        return order is null ? Error(404, "PRODUCTION_ORDER_NOT_FOUND") :
            Results.Ok(ToResponse(order));
    }

    private static async Task<IResult> CreateAsync(CreateOrderRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.OperationId == Guid.Empty ||
            request.ClassifierId.HasValue == request.CorrectsOrderId.HasValue ||
            request.ClassifierId == Guid.Empty || request.CorrectsOrderId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var existing = await db.ProductionOrders.Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                    item.BranchId == context.BranchId &&
                    item.OperationId == request.OperationId, cancellationToken);
            if (existing is not null)
                return existing.ClassifierId == request.ClassifierId &&
                       existing.CorrectsOrderId == request.CorrectsOrderId
                    ? Results.Ok(ToResponse(existing)) :
                    Error(409, "PRODUCTION_OPERATION_ID_REUSED");
            if (request.ClassifierId is Guid classifierId &&
                !await db.ProductionClassifiers.AsNoTracking().AnyAsync(item =>
                    item.CompanyId == context.CompanyId && item.Id == classifierId &&
                    item.IsActive, cancellationToken))
                return Error(409, "PRODUCTION_CLASSIFIER_NOT_AVAILABLE");
            if (request.CorrectsOrderId is Guid correctsOrderId &&
                !await db.ProductionOrders.AsNoTracking().AnyAsync(item =>
                    item.CompanyId == context.CompanyId &&
                    item.BranchId == context.BranchId && item.Id == correctsOrderId &&
                    item.Status == ProductionOrderStatus.Closed, cancellationToken))
                return Error(409, "PRODUCTION_ORIGINAL_NOT_CLOSED");
            var order = new ProductionOrder(context.CompanyId, context.BranchId,
                request.OperationId, context.UserId, clock.GetUtcNow(),
                request.ClassifierId, request.CorrectsOrderId);
            db.ProductionOrders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Created($"/api/production/orders/{order.Id}", ToResponse(order));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        { return Error(409, "PRODUCTION_ORDER_CONFLICT"); }
    }

    private static Task<ProductionOrder?> FindAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, Guid id,
        CancellationToken cancellationToken) =>
        db.ProductionOrders.Include(item => item.Lines).SingleOrDefaultAsync(item =>
            item.CompanyId == accessor.Context.CompanyId &&
            item.BranchId == accessor.Context.BranchId && item.Id == id,
            cancellationToken);

    private static OrderResponse ToResponse(ProductionOrder item) => new(
        item.Id, item.OperationId, item.BranchId, item.Kind.ToString(),
        item.Status.ToString(), item.ClassifierId, item.CorrectsOrderId,
        item.Version, item.CreatedByUserId, item.CreatedAtUtc,
        item.ClosedByUserId, item.ClosedAtUtc, item.CanceledByUserId,
        item.CanceledAtUtc, item.InputMassKg, item.OutputMassKg,
        item.ReportedWasteKg, item.MassDifferenceKg, item.WasteReason,
        item.Lines.Select(line => new LineResponse(line.Id, line.ProductId,
            line.ProductCode, line.ProductName, line.Unit,
            line.SaleMode.ToString(), line.Direction.ToString(),
            line.Quantity, line.MassKg, line.InputPieceId,
            line.OutputBarcodeProfileId, line.OutputSourceSystem,
            line.OutputExternalIdentifier, line.OutputRawBarcode,
            line.OutputIdentifierField, line.InventoryMovementId,
            line.OutputPieceId)).ToArray());

    private static bool IsConcurrencyConflict(Exception exception) =>
        exception is InvalidOperationException { InnerException: PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected } } or
            DbUpdateConcurrencyException or
            DbUpdateException { InnerException: PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation or
                    PostgresErrorCodes.SerializationFailure or
                    PostgresErrorCodes.DeadlockDetected } } or
            PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected };

    private static bool AllowedOrigin(HttpContext http)
    {
        var origin = http.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) ||
            RequestOriginValidator.IsAllowed(origin, http.Request);
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: status);

    private sealed record CreateOrderRequest(Guid OperationId, Guid? ClassifierId,
        Guid? CorrectsOrderId);
    private sealed record OrderPage(OrderResponse[] Items, int Page, int PageSize,
        long TotalItems, long TotalPages);
    private sealed record LineResponse(Guid Id, Guid ProductId, string ProductCode,
        string ProductName, string Unit, string SaleMode, string Direction,
        decimal Quantity, decimal MassKg, Guid? InputPieceId,
        Guid? OutputBarcodeProfileId, string? OutputSourceSystem,
        string? OutputExternalIdentifier, string? OutputRawBarcode,
        string? OutputIdentifierField, Guid? InventoryMovementId,
        Guid? OutputPieceId);
    private sealed record OrderResponse(Guid Id, Guid OperationId, Guid BranchId,
        string Kind, string Status, Guid? ClassifierId, Guid? CorrectsOrderId,
        int Version, Guid CreatedByUserId, DateTimeOffset CreatedAtUtc,
        Guid? ClosedByUserId, DateTimeOffset? ClosedAtUtc,
        Guid? CanceledByUserId, DateTimeOffset? CanceledAtUtc,
        decimal InputMassKg, decimal OutputMassKg, decimal ReportedWasteKg,
        decimal MassDifferenceKg, string WasteReason, LineResponse[] Lines);
}
