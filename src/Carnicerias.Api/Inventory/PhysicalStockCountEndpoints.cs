using System.Data;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class PhysicalStockCountEndpoints
{
    public static IEndpointRouteBuilder MapPhysicalStockCountEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var counts = endpoints.MapGroup("/api/inventory/counts")
            .RequireOperationalPermission(PlatformPermissionCatalog.InventoryStockManage);
        counts.MapGet("", ListAsync);
        counts.MapGet("/policy", PolicyOptionsAsync);
        counts.MapGet("/{id:guid}", GetAsync);
        counts.MapPost("", CreateAsync);
        counts.MapPost("/{id:guid}/lines", AddLineAsync);
        counts.MapPost("/{id:guid}/submit", SubmitAsync);
        counts.MapPost("/{id:guid}/approve", ApproveAsync);
        counts.MapPost("/{id:guid}/reject", RejectAsync);
        counts.MapPost("/{id:guid}/cancel", CancelAsync);
        endpoints.MapPatch("/api/inventory/count-policy/categories/{id:guid}",
            SetCategoryPolicyAsync).RequireOperationalPermission(
                PlatformPermissionCatalog.CatalogManage);
        endpoints.MapPatch("/api/inventory/count-policy/products/{id:guid}",
            SetProductPolicyAsync).RequireOperationalPermission(
                PlatformPermissionCatalog.CatalogManage);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var query = db.PhysicalStockCounts.AsNoTracking().Where(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new CountSummary(item.Id, item.Status.ToString(),
                item.Version, item.CreatedAtUtc, item.SubmittedAtUtc,
                item.DecidedAtUtc)).ToArrayAsync(cancellationToken);
        return Results.Ok(new CountPage(items, page, pageSize, total,
            total == 0 ? 0 : (total - 1) / pageSize + 1));
    }

    private static async Task<IResult> PolicyOptionsAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var companyId = accessor.Context.CompanyId;
        var categories = await db.ProductCategories.AsNoTracking().Where(item =>
                item.CompanyId == companyId && item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new CategoryPolicy(item.Id, item.Name,
                item.CountPolicy.ToString())).ToArrayAsync(cancellationToken);
        var products = await db.CatalogProducts.AsNoTracking().Where(item =>
                item.CompanyId == companyId && item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new ProductPolicy(item.Id, item.CategoryId,
                item.Code, item.Name, item.Unit,
                item.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
                item.CountPolicyOverride.HasValue
                    ? item.CountPolicyOverride.Value.ToString() : null))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new PolicyOptions(categories, products));
    }

    private static async Task<IResult> SetCategoryPolicyAsync(Guid id,
        PolicyRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || !Enum.TryParse<StockCountPolicy>(request.Policy,
                false, out var policy) || !Enum.IsDefined(policy))
            return Error(400, "VALIDATION_ERROR");
        var category = await db.ProductCategories.SingleOrDefaultAsync(item =>
            item.CompanyId == accessor.Context.CompanyId && item.Id == id,
            cancellationToken);
        if (category is null) return Error(404, "CATEGORY_NOT_FOUND");
        category.SetCountPolicy(policy);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new CategoryPolicy(category.Id, category.Name,
            category.CountPolicy.ToString()));
    }

    private static async Task<IResult> SetProductPolicyAsync(Guid id,
        ProductPolicyRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.PolicyOverride is not null &&
            (!Enum.TryParse<StockCountPolicy>(request.PolicyOverride,
                false, out var parsed) || !Enum.IsDefined(parsed)))
            return Error(400, "VALIDATION_ERROR");
        var product = await db.CatalogProducts.SingleOrDefaultAsync(item =>
            item.CompanyId == accessor.Context.CompanyId && item.Id == id,
            cancellationToken);
        if (product is null) return Error(404, "PRODUCT_NOT_FOUND");
        if (product.SaleMode == ProductSaleMode.Unit &&
            request.PolicyOverride == nameof(StockCountPolicy.MassObservation))
            return Error(400, "MASS_POLICY_REQUIRES_WEIGHT_PRODUCT");
        product.SetCountPolicyOverride(request.PolicyOverride is null ? null :
            Enum.Parse<StockCountPolicy>(request.PolicyOverride));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ProductPolicy(product.Id, product.CategoryId,
            product.Code, product.Name, product.Unit,
            product.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
            product.CountPolicyOverride?.ToString()));
    }

    private static async Task<IResult> GetAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var count = await FindAsync(db, accessor, id, false, cancellationToken);
        return count is null ? Error(404, "COUNT_NOT_FOUND") :
            Results.Ok(ToResponse(count));
    }

    private static async Task<IResult> CreateAsync(CreateRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.OperationId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        var existing = await db.PhysicalStockCounts.Include(item => item.Lines)
            .AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.OperationId == request.OperationId, cancellationToken);
        if (existing is not null) return Results.Ok(ToResponse(existing));
        var count = new PhysicalStockCount(context.CompanyId, context.BranchId,
            request.OperationId, context.UserId, clock.GetUtcNow());
        db.PhysicalStockCounts.Add(count);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/inventory/counts/{count.Id}", ToResponse(count));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "COUNT_CONFLICT");
        }
    }

    private static async Task<IResult> AddLineAsync(Guid id, AddLineRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1 || request.ProductId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var count = await FindAsync(db, accessor, id, true, cancellationToken);
            if (count is null) return Error(404, "COUNT_NOT_FOUND");
            if (count.Status != PhysicalStockCountStatus.Draft)
                return Error(409, "COUNT_NOT_DRAFT");
            if (count.Version != request.Version)
                return Error(409, "COUNT_VERSION_CHANGED");
            if (count.Lines.Any(line => line.ProductId == request.ProductId))
                return Error(409, "PRODUCT_ALREADY_COUNTED");
            var product = await db.CatalogProducts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.Id == request.ProductId &&
                item.IsActive, cancellationToken);
            if (product is null) return Error(404, "PRODUCT_NOT_AVAILABLE");
            var categoryPolicy = await db.ProductCategories.AsNoTracking().Where(item =>
                item.CompanyId == context.CompanyId && item.Id == product.CategoryId)
                .Select(item => item.CountPolicy).SingleAsync(cancellationToken);
            var policy = EffectivePolicy(product, categoryPolicy);
            var balance = await db.BranchInventoryBalances.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                    item.BranchId == context.BranchId && item.ProductId == product.Id,
                    cancellationToken);
            var last = await db.InventoryMovements.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                    item.ProductId == product.Id)
                .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
                .Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
            PhysicalStockCountLine line;
            try
            {
                line = new PhysicalStockCountLine(count.Id, product, policy,
                    request.CountedQuantity, balance?.OnHand ?? 0,
                    balance?.Reserved ?? 0, balance?.ProtectedOnHand ?? 0,
                    balance?.ProtectedReserved ?? 0, balance?.Quarantined ?? 0,
                    last, context.UserId, clock.GetUtcNow());
                count.AddLine(line);
            }
            catch (Exception exception) when (exception is ArgumentException or
                InvalidOperationException)
            { return Error(400, "INVALID_COUNT_LINE"); }
            db.PhysicalStockCountLines.Add(line);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(count));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "COUNT_CONFLICT");
        }
    }

    private static async Task<IResult> SubmitAsync(Guid id, VersionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1) return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var count = await FindAsync(db, accessor, id, true, cancellationToken);
            if (count is null) return Error(404, "COUNT_NOT_FOUND");
            if (count.Status == PhysicalStockCountStatus.Submitted)
                return Results.Ok(ToResponse(count));
            if (count.Status != PhysicalStockCountStatus.Draft)
                return Error(409, "COUNT_NOT_DRAFT");
            if (count.Version != request.Version)
                return Error(409, "COUNT_VERSION_CHANGED");
            if (count.Lines.Count == 0) return Error(400, "COUNT_EMPTY");
            count.Submit(accessor.Context.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(count));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        { return Error(409, "COUNT_CONFLICT"); }
    }

    private static async Task<IResult> ApproveAsync(Guid id, DecisionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1 || !Required(request.Reason, 240))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (!await IsAdministratorAsync(db, context.CompanyId, context.UserId,
                cancellationToken))
            return Error(403, "ADMINISTRATOR_REQUIRED");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var count = await FindAsync(db, accessor, id, true, cancellationToken);
            if (count is null) return Error(404, "COUNT_NOT_FOUND");
            if (count.Status == PhysicalStockCountStatus.Approved)
                return Results.Ok(ToResponse(count));
            if (count.Status != PhysicalStockCountStatus.Submitted)
                return Error(409, "COUNT_NOT_SUBMITTED");
            if (count.Version != request.Version)
                return Error(409, "COUNT_VERSION_CHANGED");
            var productIds = count.Lines.Select(line => line.ProductId).ToArray();
            var products = await db.CatalogProducts.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && productIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            var categoryIds = products.Values.Select(item => item.CategoryId).Distinct().ToArray();
            var categoryPolicies = await db.ProductCategories.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId && categoryIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.CountPolicy,
                    cancellationToken);
            var balances = await db.BranchInventoryBalances.AsNoTracking().Where(item =>
                    item.CompanyId == context.CompanyId &&
                    item.BranchId == context.BranchId && productIds.Contains(item.ProductId))
                .ToDictionaryAsync(item => item.ProductId, cancellationToken);
            var now = clock.GetUtcNow();
            foreach (var line in count.Lines.OrderBy(item => item.ProductId))
            {
                if (!products.TryGetValue(line.ProductId, out var product) ||
                    !product.IsActive || product.Unit != line.Unit ||
                    product.SaleMode != line.SaleMode ||
                    !categoryPolicies.TryGetValue(product.CategoryId, out var categoryPolicy))
                    return Error(409, "COUNT_POLICY_OR_PRODUCT_CHANGED");
                balances.TryGetValue(line.ProductId, out var balance);
                var currentLast = await db.InventoryMovements.AsNoTracking().Where(item =>
                        item.CompanyId == context.CompanyId &&
                        item.BranchId == context.BranchId &&
                        item.ProductId == line.ProductId)
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .ThenByDescending(item => item.Id)
                    .Select(item => (Guid?)item.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!line.MatchesCurrent(balance, currentLast,
                        EffectivePolicy(product, categoryPolicy)))
                    return Error(409, "COUNT_STALE_RECOUNT_REQUIRED");
                if (await StockReconciliationLease.IsLockedForUpdateAsync(db,
                        context.CompanyId, context.BranchId, line.ProductId, now,
                        cancellationToken))
                    return Error(409, "ARTICLE_RECONCILIATION_IN_PROGRESS");
                if (line.Policy != StockCountPolicy.TheoreticalStock || line.Delta == 0)
                    continue;
                if (!line.CanApplyStockDifference())
                    return Error(409, "COUNT_INVADES_PROTECTED_STOCK");
                if (balance is null)
                {
                    if (line.SystemOnHand != 0 || line.SystemReserved != 0 ||
                        line.SystemProtectedOnHand != 0 || line.SystemQuarantined != 0 ||
                        line.CountedQuantity <= 0)
                        return Error(409, "COUNT_STALE_RECOUNT_REQUIRED");
                    var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO inventory.branch_inventory
                            ("CompanyId", "BranchId", "ProductId", "OnHand", "Reserved", "ProtectedOnHand", "ProtectedReserved")
                        VALUES ({context.CompanyId}, {context.BranchId}, {line.ProductId},
                            {line.CountedQuantity}, 0, 0, 0)
                        ON CONFLICT ("CompanyId", "BranchId", "ProductId") DO NOTHING
                        """, cancellationToken);
                    if (inserted != 1)
                        return Error(409, "COUNT_STALE_RECOUNT_REQUIRED");
                }
                else
                {
                    var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE inventory.branch_inventory
                        SET "OnHand" = {line.CountedQuantity}
                        WHERE "CompanyId" = {context.CompanyId} AND
                            "BranchId" = {context.BranchId} AND
                            "ProductId" = {line.ProductId} AND
                            "OnHand" = {line.SystemOnHand} AND
                            "Reserved" = {line.SystemReserved} AND
                            "ProtectedOnHand" = {line.SystemProtectedOnHand} AND
                            "ProtectedReserved" = {line.SystemProtectedReserved} AND
                            "Quarantined" = {line.SystemQuarantined} AND
                            ("ReconciliationLockExpiresAtUtc" IS NULL OR
                                "ReconciliationLockExpiresAtUtc" <= {now})
                        """, cancellationToken);
                    if (changed != 1)
                        return Error(409, "COUNT_STALE_RECOUNT_REQUIRED");
                }
            }
            count.Approve(context.UserId, request.Reason, now);
            foreach (var line in count.Lines.Where(line =>
                line.Policy == StockCountPolicy.TheoreticalStock && line.Delta != 0))
            {
                var movement = new InventoryMovement(context.CompanyId,
                    context.BranchId, line.ProductId, context.UserId, line.Id,
                    InventoryMovementKind.PhysicalCountAdjustment, line.Delta,
                    $"Conteo físico {count.Id:N}", now);
                db.InventoryMovements.Add(movement);
                count.LinkMovement(line.Id, movement.Id);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(count));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        { return Error(409, "COUNT_CONFLICT"); }
    }

    private static async Task<IResult> RejectAsync(Guid id, DecisionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1 || !Required(request.Reason, 240))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (!await IsAdministratorAsync(db, context.CompanyId, context.UserId,
                cancellationToken))
            return Error(403, "ADMINISTRATOR_REQUIRED");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var count = await FindAsync(db, accessor, id, true, cancellationToken);
            if (count is null) return Error(404, "COUNT_NOT_FOUND");
            if (count.Status == PhysicalStockCountStatus.Rejected)
                return Results.Ok(ToResponse(count));
            if (count.Status != PhysicalStockCountStatus.Submitted)
                return Error(409, "COUNT_NOT_SUBMITTED");
            if (count.Version != request.Version)
                return Error(409, "COUNT_VERSION_CHANGED");
            count.Reject(context.UserId, request.Reason, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(count));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        { return Error(409, "COUNT_CONFLICT"); }
    }

    private static async Task<IResult> CancelAsync(Guid id, VersionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Version < 1) return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var count = await FindAsync(db, accessor, id, true, cancellationToken);
            if (count is null) return Error(404, "COUNT_NOT_FOUND");
            if (count.Status == PhysicalStockCountStatus.Canceled)
                return Results.Ok(ToResponse(count));
            if (count.Status != PhysicalStockCountStatus.Draft)
                return Error(409, "COUNT_NOT_DRAFT");
            if (count.Version != request.Version)
                return Error(409, "COUNT_VERSION_CHANGED");
            count.Cancel(accessor.Context.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(count));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        { return Error(409, "COUNT_CONFLICT"); }
    }

    private static Task<PhysicalStockCount?> FindAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, Guid id, bool tracked,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var query = db.PhysicalStockCounts.Include(item => item.Lines).AsQueryable();
        if (!tracked) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(item => item.Id == id &&
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId,
            cancellationToken);
    }

    private static CountResponse ToResponse(PhysicalStockCount count) => new(
        count.Id, count.OperationId, count.BranchId, count.Status.ToString(),
        count.Version, count.CreatedByUserId, count.CreatedAtUtc,
        count.SubmittedByUserId, count.SubmittedAtUtc, count.DecidedByUserId,
        count.DecidedAtUtc, count.DecisionReason,
        count.Lines.Where(line => line.Policy == StockCountPolicy.MassObservation &&
            line.SaleMode == ProductSaleMode.Weight)
            .Sum(line => line.CountedQuantity),
        count.Lines.Where(line => line.Policy == StockCountPolicy.MassObservation &&
            line.SaleMode == ProductSaleMode.Weight)
            .Sum(line => line.SystemOnHand),
        count.Lines.Select(line => new CountLineResponse(line.Id, line.ProductId,
            line.ProductCode, line.ProductName, line.AnatomicalGroup, line.Unit,
            line.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
            line.Policy.ToString(), line.SystemOnHand, line.SystemReserved,
            line.SystemProtectedOnHand, line.SystemQuarantined,
            line.CountedQuantity, line.Delta, line.CountedByUserId,
            line.CountedAtUtc, line.InventoryMovementId)).ToArray(),
        count.Lines.Where(line => line.Policy == StockCountPolicy.MassObservation &&
                line.SaleMode == ProductSaleMode.Weight)
            .GroupBy(line => line.AnatomicalGroup, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AnatomicalMassResponse(group.Key, group.Count(),
                group.Sum(line => line.CountedQuantity)))
            .OrderBy(group => group.Group, StringComparer.OrdinalIgnoreCase).ToArray());

    private static Task<bool> IsAdministratorAsync(PlatformAccessDbContext db,
        Guid companyId, Guid userId, CancellationToken cancellationToken) =>
        db.UserAssignments.AsNoTracking().AnyAsync(assignment =>
            assignment.CompanyId == companyId && assignment.UserId == userId &&
            assignment.BranchId == null && db.Roles.Any(role =>
                role.Id == assignment.RoleId && role.Code == "administrator"),
            cancellationToken);

    private static StockCountPolicy EffectivePolicy(CatalogProduct product,
        StockCountPolicy categoryPolicy) => product.SaleMode == ProductSaleMode.Unit
        ? StockCountPolicy.TheoreticalStock :
        product.CountPolicyOverride ?? categoryPolicy;

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin,
            context.Request);
    }

    private static bool Required(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= limit;

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
        new ErrorResponse(new ApiError(code, "No se pudo procesar el conteo físico", [])),
        statusCode: status);

    private sealed record CreateRequest(Guid OperationId);
    private sealed record AddLineRequest(int Version, Guid ProductId,
        decimal CountedQuantity);
    private sealed record VersionRequest(int Version);
    private sealed record DecisionRequest(int Version, string Reason);
    private sealed record PolicyRequest(string Policy);
    private sealed record ProductPolicyRequest(string? PolicyOverride);
    private sealed record CategoryPolicy(Guid Id, string Name, string Policy);
    private sealed record ProductPolicy(Guid Id, Guid CategoryId, string Code,
        string Name, string Unit, string SaleMode, string? PolicyOverride);
    private sealed record PolicyOptions(IReadOnlyList<CategoryPolicy> Categories,
        IReadOnlyList<ProductPolicy> Products);
    private sealed record CountSummary(Guid Id, string Status, int Version,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? SubmittedAtUtc,
        DateTimeOffset? DecidedAtUtc);
    private sealed record CountPage(IReadOnlyList<CountSummary> Items, int Page,
        int PageSize, long TotalItems, long TotalPages);
    private sealed record CountLineResponse(Guid Id, Guid ProductId,
        string ProductCode, string ProductName, string? AnatomicalGroup,
        string Unit, string SaleMode,
        string Policy, decimal SystemOnHand, decimal SystemReserved,
        decimal SystemProtectedOnHand, decimal SystemQuarantined,
        decimal CountedQuantity, decimal Delta, Guid CountedByUserId,
        DateTimeOffset CountedAtUtc, Guid? InventoryMovementId);
    private sealed record CountResponse(Guid Id, Guid OperationId, Guid BranchId,
        string Status, int Version, Guid CreatedByUserId,
        DateTimeOffset CreatedAtUtc, Guid? SubmittedByUserId,
        DateTimeOffset? SubmittedAtUtc, Guid? DecidedByUserId,
        DateTimeOffset? DecidedAtUtc, string? DecisionReason,
        decimal MassObservedKg, decimal MassLedgerReferenceKg,
        IReadOnlyList<CountLineResponse> Lines,
        IReadOnlyList<AnatomicalMassResponse> AnatomicalMasses);
    private sealed record AnatomicalMassResponse(string? Group, int ArticleCount,
        decimal ObservedKg);
}
