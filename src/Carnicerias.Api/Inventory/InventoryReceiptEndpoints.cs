using System.Data;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.ExcelImport;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class InventoryReceiptEndpoints
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapInventoryReceiptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var receipts = endpoints.MapGroup("/api/inventory/receipts")
            .RequireOperationalPermission(PlatformPermissionCatalog.InventoryReceiptsManage);
        receipts.MapGet("", ListAsync);
        receipts.MapGet("/supplier-options", SupplierOptionsAsync);
        receipts.MapGet("/product-options", ProductOptionsAsync);
        receipts.MapGet("/{id:guid}", GetAsync);
        receipts.MapPost("", CreateAsync);
        receipts.MapPut("/{id:guid}/lines", ReplaceLinesAsync);
        receipts.MapPost("/{id:guid}/approve", ApproveAsync);
        receipts.MapPost("/{id:guid}/cancel", CancelAsync);
        receipts.MapGet("/{id:guid}/import/template", ImportTemplateAsync);
        receipts.MapPost("/{id:guid}/import/preview", PreviewImportAsync);
        receipts.MapPost("/{id:guid}/import/apply", ApplyImportAsync);
        endpoints.MapPost("/api/inventory/receipts/{id:guid}/lines/{lineId:guid}/resolve",
            ResolveDifferenceAsync).RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> SupplierOptionsAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var items = await db.Suppliers.AsNoTracking().Where(item =>
                item.CompanyId == accessor.Context.CompanyId && item.IsActive)
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Select(item => new SupplierOption(item.Id, item.Code, item.Name))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> ProductOptionsAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var items = await db.CatalogProducts.AsNoTracking().Where(item =>
                item.CompanyId == accessor.Context.CompanyId && item.IsActive)
            .OrderBy(item => item.Code).ThenBy(item => item.Id)
            .Select(item => new ReceiptProductOption(item.Id, item.Code, item.Name,
                item.Unit, item.SaleMode == ProductSaleMode.Unit ? "unit" : "weight"))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items);
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
        var query = db.InventoryReceipts.AsNoTracking().Where(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId);
        if (status is not null)
        {
            var requested = Enum.Parse<InventoryReceiptStatus>(status);
            query = query.Where(item => item.Status == requested);
        }
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new ReceiptSummary(item.Id, item.OperationId, item.SupplierName,
                item.SourceSystem, item.DocumentType, item.DocumentNumber, item.DocumentDate,
                item.Status.ToString(), item.Version, item.CreatedAtUtc,
                item.ApprovedAtUtc))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new ReceiptPage(items, page, pageSize, total,
            total == 0 ? 0 : (total - 1) / pageSize + 1));
    }

    private static async Task<IResult> GetAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var receipt = await FindAsync(db, accessor, id, false, cancellationToken);
        return receipt is null ? Error(404, "RECEIPT_NOT_FOUND") : Results.Ok(ToResponse(receipt));
    }

    private static async Task<IResult> CreateAsync(CreateReceiptRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.OperationId == Guid.Empty || request.SupplierId == Guid.Empty ||
            request.DocumentDate == default || !Required(request.DocumentType, 40) ||
            !Required(request.DocumentNumber, 80) || !Optional(request.Destination, 120) ||
            !Optional(request.ExternalReference, 120))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var existing = await db.InventoryReceipts.Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.OperationId == request.OperationId,
                cancellationToken);
        if (existing is not null)
            return existing.SupplierId == request.SupplierId &&
                   existing.DocumentType == request.DocumentType.Trim() &&
                   existing.DocumentNumber == request.DocumentNumber.Trim() &&
                   existing.DocumentDate == request.DocumentDate &&
                   existing.Destination == Normalize(request.Destination) &&
                   existing.ExternalReference == Normalize(request.ExternalReference)
                ? Results.Ok(ToResponse(existing)) : Error(409, "OPERATION_ID_REUSED");
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.Id == request.SupplierId && item.IsActive,
            cancellationToken);
        if (supplier is null) return Error(404, "SUPPLIER_NOT_AVAILABLE");
        var receipt = new InventoryReceipt(context.CompanyId, context.BranchId,
            request.OperationId, supplier, request.DocumentType, request.DocumentNumber,
            request.DocumentDate, context.UserId, clock.GetUtcNow(), request.Destination,
            request.ExternalReference);
        db.InventoryReceipts.Add(receipt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/inventory/receipts/{receipt.Id}", ToResponse(receipt));
        }
        catch (DbUpdateException exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "OPERATION_ID_REUSED");
        }
    }

    private static async Task<IResult> ReplaceLinesAsync(Guid id, ReplaceLinesRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request?.Lines is null || request.Lines.Count > 500 || request.Version < 1 ||
            request.Lines.Any(line => line.ProductId == Guid.Empty))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "RECEIPT_NOT_FOUND");
            if (receipt.Status != InventoryReceiptStatus.Draft)
                return Error(409, "RECEIPT_NOT_DRAFT");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            var ids = request.Lines.Select(line => line.ProductId).Distinct().ToArray();
            var products = await db.CatalogProducts.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && ids.Contains(item.Id) && item.IsActive)
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (products.Count != ids.Length) return Error(404, "PRODUCT_NOT_AVAILABLE");
            var lines = new List<InventoryReceiptLine>(request.Lines.Count);
            foreach (var input in request.Lines)
            {
                try
                {
                    lines.Add(new InventoryReceiptLine(receipt.Id, products[input.ProductId],
                        input.ReceivedQuantity, input.ExpectedQuantity));
                }
                catch (ArgumentException) { return Error(400, "INVALID_PRODUCT_QUANTITY"); }
            }
            db.InventoryReceiptLines.RemoveRange(receipt.Lines);
            receipt.ReplaceLines(lines);
            db.InventoryReceiptLines.AddRange(lines);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_VERSION_CHANGED");
        }
    }

    private static async Task<IResult> ImportTemplateAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var receipt = await FindAsync(db, accessor, id, false, cancellationToken);
        if (receipt is null) return Error(404, "RECEIPT_NOT_FOUND");
        if (receipt.Status != InventoryReceiptStatus.Draft)
            return Error(409, "RECEIPT_NOT_DRAFT");
        return Results.File(AdminExcelWorkbook.CreateTemplate("receipts"),
            XlsxContentType, "plantilla-recepcion.xlsx");
    }

    private static async Task<IResult> PreviewImportAsync(Guid id, int version,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (version < 1) return Error(400, "VALIDATION_ERROR");
        var receipt = await FindAsync(db, accessor, id, false, cancellationToken);
        if (receipt is null) return Error(404, "RECEIPT_NOT_FOUND");
        if (receipt.Status != InventoryReceiptStatus.Draft)
            return Error(409, "RECEIPT_NOT_DRAFT");
        if (receipt.Version != version) return Error(409, "RECEIPT_VERSION_CHANGED");
        var upload = await ReadSpreadsheetAsync(http.Request, cancellationToken);
        if (upload.Error is not null) return upload.Error;
        var plan = await PlanImportAsync(db, receipt, upload.Bytes!, cancellationToken);
        return Results.Json(plan.Preview, statusCode: plan.Preview.Issues.Count == 0 ? 200 : 400);
    }

    private static async Task<IResult> ApplyImportAsync(Guid id, int version,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (version < 1) return Error(400, "VALIDATION_ERROR");
        var upload = await ReadSpreadsheetAsync(http.Request, cancellationToken);
        if (upload.Error is not null) return upload.Error;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var receipt = await FindAsync(db, accessor, id, true, cancellationToken);
            if (receipt is null) return Error(404, "RECEIPT_NOT_FOUND");
            if (receipt.Status != InventoryReceiptStatus.Draft)
                return Error(409, "RECEIPT_NOT_DRAFT");
            if (receipt.Version != version) return Error(409, "RECEIPT_VERSION_CHANGED");
            var plan = await PlanImportAsync(db, receipt, upload.Bytes!, cancellationToken);
            if (!plan.Preview.CanApply)
                return Results.Json(plan.Preview, statusCode: 400);
            db.InventoryReceiptLines.RemoveRange(receipt.Lines);
            receipt.ReplaceLines(plan.Lines);
            db.InventoryReceiptLines.AddRange(plan.Lines);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_VERSION_CHANGED");
        }
    }

    private static async Task<ReceiptImportPlan> PlanImportAsync(PlatformAccessDbContext db,
        InventoryReceipt receipt, byte[] bytes, CancellationToken cancellationToken)
    {
        var parsed = AdminExcelWorkbook.Read("receipts", bytes);
        if (parsed.Issues.Count != 0)
            return ReceiptImportPlanner.Plan(receipt.CompanyId, receipt.Id, parsed, [], []);
        var codes = parsed.Rows.Select(row => row.Text("CodigoArticulo"))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.Trim().ToUpperInvariant()).Distinct().ToArray();
        var alternatives = await db.ProductCodes.AsNoTracking().Where(code =>
                code.CompanyId == receipt.CompanyId && code.IsActive &&
                codes.Contains(code.NormalizedCode))
            .ToArrayAsync(cancellationToken);
        var alternateIds = alternatives.Select(code => code.ProductId).ToArray();
        var products = await db.CatalogProducts.AsNoTracking().Where(product =>
                product.CompanyId == receipt.CompanyId &&
                (codes.Contains(product.NormalizedCode) || alternateIds.Contains(product.Id)))
            .ToArrayAsync(cancellationToken);
        return ReceiptImportPlanner.Plan(receipt.CompanyId, receipt.Id, parsed,
            products, alternatives);
    }

    private static async Task<(byte[]? Bytes, IResult? Error)> ReadSpreadsheetAsync(
        HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentType?.Split(';', 2)[0].Trim().Equals(XlsxContentType,
                StringComparison.OrdinalIgnoreCase) != true)
            return (null, Error(400, "INVALID_CONTENT_TYPE"));
        if (request.ContentLength > AdminExcelWorkbook.MaxFileBytes)
            return (null, Error(413, "IMPORT_TOO_LARGE"));
        using var stream = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (stream.Length + read > AdminExcelWorkbook.MaxFileBytes)
                return (null, Error(413, "IMPORT_TOO_LARGE"));
            stream.Write(buffer, 0, read);
        }
        return (stream.ToArray(), null);
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
            if (receipt is null) return Error(404, "RECEIPT_NOT_FOUND");
            if (receipt.Status == InventoryReceiptStatus.Approved)
                return Results.Ok(ToResponse(receipt));
            if (receipt.Status != InventoryReceiptStatus.Draft)
                return Error(409, "RECEIPT_NOT_DRAFT");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            var positive = receipt.Lines.Where(line => line.ReceivedQuantity > 0).ToArray();
            if (positive.Length == 0) return Error(400, "RECEIPT_HAS_NO_QUANTITY");
            var ids = positive.Select(line => line.ProductId).Distinct().ToArray();
            var activeProducts = await db.CatalogProducts.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && ids.Contains(item.Id) && item.IsActive)
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (activeProducts.Count != ids.Length || positive.Any(line =>
                    activeProducts[line.ProductId].SaleMode != line.SaleMode ||
                    activeProducts[line.ProductId].Unit != line.Unit))
                return Error(409, "PRODUCT_NOT_AVAILABLE");
            var now = clock.GetUtcNow();
            foreach (var group in positive.GroupBy(line => line.ProductId).OrderBy(group => group.Key))
            {
                if (await StockReconciliationLease.IsLockedForUpdateAsync(db, context.CompanyId,
                        context.BranchId, group.Key, now, cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
                var quantity = group.Sum(line => line.ReceivedQuantity);
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO inventory.branch_inventory
                        ("CompanyId", "BranchId", "ProductId", "OnHand", "Reserved")
                    VALUES ({context.CompanyId}, {context.BranchId}, {group.Key}, {quantity}, 0)
                    ON CONFLICT ("CompanyId", "BranchId", "ProductId") DO UPDATE
                        SET "OnHand" = inventory.branch_inventory."OnHand" + EXCLUDED."OnHand"
                    WHERE inventory.branch_inventory."ReconciliationLockExpiresAtUtc" IS NULL OR
                        inventory.branch_inventory."ReconciliationLockExpiresAtUtc" <= {now}
                    """, cancellationToken);
                if (changed != 1) return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
            }
            receipt.Approve(context.UserId, now);
            foreach (var line in positive)
            {
                var movement = new InventoryMovement(context.CompanyId, context.BranchId,
                    line.ProductId, context.UserId, line.Id, InventoryMovementKind.Receipt,
                    line.ReceivedQuantity, $"Recepción {receipt.DocumentType} {receipt.DocumentNumber}", now);
                db.InventoryMovements.Add(movement);
                receipt.LinkMovement(line.Id, movement.Id);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_CONFLICT");
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
            if (receipt is null) return Error(404, "RECEIPT_NOT_FOUND");
            if (receipt.Status == InventoryReceiptStatus.Canceled)
                return Results.Ok(ToResponse(receipt));
            if (receipt.Status != InventoryReceiptStatus.Draft)
                return Error(409, "RECEIPT_NOT_DRAFT");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            receipt.Cancel(accessor.Context.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_CONFLICT");
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
            if (receipt is null) return Error(404, "RECEIPT_NOT_FOUND");
            var line = receipt.Lines.SingleOrDefault(item => item.Id == lineId);
            if (line is null) return Error(404, "RECEIPT_LINE_NOT_FOUND");
            if (line.DifferenceStatus == InventoryReceiptDifferenceStatus.Resolved)
                return line.DifferenceReason == request.Reason.Trim()
                    ? Results.Ok(ToResponse(receipt)) : Error(409, "DIFFERENCE_ALREADY_RESOLVED");
            if (receipt.Version != request.Version) return Error(409, "RECEIPT_VERSION_CHANGED");
            if (line.DifferenceStatus != InventoryReceiptDifferenceStatus.PendingReview)
                return Error(409, "DIFFERENCE_NOT_PENDING");
            receipt.ResolveDifference(lineId, context.UserId, request.Reason, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "RECEIPT_CONFLICT");
        }
    }

    private static Task<InventoryReceipt?> FindAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, Guid id, bool tracked,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var query = db.InventoryReceipts.Include(item => item.Lines).AsQueryable();
        if (!tracked) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(item => item.Id == id &&
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId,
            cancellationToken);
    }

    private static ReceiptResponse ToResponse(InventoryReceipt receipt) => new(receipt.Id,
        receipt.OperationId, receipt.SupplierId, receipt.SupplierCode, receipt.SupplierName,
        receipt.SupplierIdentification, receipt.SourceSystem, receipt.DocumentType,
        receipt.DocumentNumber, receipt.DocumentDate, receipt.Destination,
        receipt.ExternalReference, receipt.Status.ToString(), receipt.Version,
        receipt.CreatedAtUtc, receipt.ApprovedAtUtc, receipt.CanceledAtUtc,
        receipt.Lines.Select(line => new ReceiptLineResponse(line.Id, line.ProductId,
            line.ProductCode, line.ProductName, line.Unit, line.SaleMode == ProductSaleMode.Unit
                ? "unit" : "weight", line.ExpectedQuantity, line.ReceivedQuantity,
            line.DifferenceStatus.ToString(), line.DifferenceReason,
            line.InventoryMovementId)).ToArray());

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static bool Required(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;
    private static bool Optional(string? value, int max) => value is null || value.Trim().Length <= max;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim();
    private static bool IsConcurrencyConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException ||
        exception is DbUpdateException { InnerException: PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected } } ||
        exception is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected };
    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo procesar la recepción", [])),
        statusCode: statusCode);

    private sealed record SupplierOption(Guid Id, string Code, string Name);
    private sealed record ReceiptProductOption(Guid ProductId, string Code, string Name,
        string Unit, string SaleMode);
    private sealed record CreateReceiptRequest(Guid OperationId, Guid SupplierId,
        string DocumentType, string DocumentNumber, DateOnly DocumentDate,
        string? Destination, string? ExternalReference);
    private sealed record ReplaceLinesRequest(int Version, IReadOnlyList<ReceiptLineInput> Lines);
    private sealed record ReceiptLineInput(Guid ProductId, decimal ReceivedQuantity,
        decimal? ExpectedQuantity);
    private sealed record VersionRequest(int Version);
    private sealed record ResolveDifferenceRequest(int Version, string Reason);
    private sealed record ReceiptSummary(Guid Id, Guid OperationId, string SupplierName,
        string? SourceSystem, string DocumentType, string DocumentNumber, DateOnly DocumentDate,
        string Status, int Version, DateTimeOffset CreatedAtUtc, DateTimeOffset? ApprovedAtUtc);
    private sealed record ReceiptPage(IReadOnlyList<ReceiptSummary> Items,
        int Page, int PageSize, long TotalItems, long TotalPages);
    private sealed record ReceiptLineResponse(Guid Id, Guid ProductId, string ProductCode,
        string ProductName, string Unit, string SaleMode, decimal? ExpectedQuantity,
        decimal ReceivedQuantity, string DifferenceStatus, string? DifferenceReason,
        Guid? InventoryMovementId);
    private sealed record ReceiptResponse(Guid Id, Guid OperationId, Guid? SupplierId,
        string SupplierCode, string SupplierName, string? SupplierIdentification,
        string? SourceSystem, string DocumentType, string DocumentNumber, DateOnly DocumentDate,
        string? Destination, string? ExternalReference, string Status, int Version,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? ApprovedAtUtc,
        DateTimeOffset? CanceledAtUtc, IReadOnlyList<ReceiptLineResponse> Lines);
}
