using System.Data;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Catalog;

public static class AdminTaxAssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAdminTaxAssignmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/tax-assignments")
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        group.MapGet("/count", CountAsync);
        group.MapGet("/{productId:guid}", HistoryAsync);
        group.MapPut("", SetAsync);
        return endpoints;
    }

    private static async Task<IResult> CountAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var total = await db.CatalogProducts.AsNoTracking().CountAsync(item =>
            item.CompanyId == accessor.Context.CompanyId, cancellationToken);
        return Results.Ok(new ProductCount(total));
    }

    private static async Task<IResult> HistoryAsync(Guid productId, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var companyId = accessor.Context.CompanyId;
        if (productId == Guid.Empty) return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (!await db.CatalogProducts.AsNoTracking().AnyAsync(item =>
                item.CompanyId == companyId && item.Id == productId, cancellationToken))
            return Results.NotFound();
        var assignments = await db.OtherTaxAssignments.AsNoTracking().Where(item =>
                item.CompanyId == companyId && item.ProductId == productId)
            .OrderByDescending(item => item.EffectiveFromUtc).Take(100)
            .ToArrayAsync(cancellationToken);
        var taxIds = assignments.Select(item => item.TaxCatalogEntryId).Distinct().ToArray();
        var taxes = await db.TaxCatalogEntries.AsNoTracking().Where(item =>
                item.CompanyId == companyId && taxIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return Results.Ok(assignments.Select(item => new OtherTaxAssignmentResponse(
            item.Id, item.ProductId, item.TaxCatalogEntryId,
            taxes[item.TaxCatalogEntryId].Name, taxes[item.TaxCatalogEntryId].RatePercent,
            item.EffectiveFromUtc, item.EffectiveToUtc,
            item.AssignedByUserId, item.RemovedByUserId)).ToArray());
    }

    private static async Task<IResult> SetAsync(SetTaxAssignmentRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider timeProvider, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || request.TaxCatalogEntryId == Guid.Empty ||
            request.IsAssigned is null ||
            request.Scope is not ("selected" or "all"))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await TaxCatalogConsistencyLock.AcquireAsync(db, request.TaxCatalogEntryId,
                cancellationToken);
            var tax = await db.TaxCatalogEntries.SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.Id == request.TaxCatalogEntryId,
                cancellationToken);
            if (tax is null) return Results.NotFound();
            if (request.IsAssigned.Value && !tax.IsActive)
                return Error(StatusCodes.Status409Conflict, "TAX_INACTIVE");
            if (tax.Kind == TaxKind.Vat && !request.IsAssigned.Value)
                return Error(StatusCodes.Status400BadRequest, "VAT_REQUIRES_TREATMENT");

            var productQuery = db.CatalogProducts.AsNoTracking()
                .Where(item => item.CompanyId == context.CompanyId);
            if (request.Scope == "selected" && request.ProductIds is { Length: > 0 } selected)
                productQuery = productQuery.Where(item => selected.Contains(item.Id));
            var ownProductIds = await productQuery.Select(item => item.Id)
                .ToArrayAsync(cancellationToken);
            Guid[] productIds;
            try
            {
                productIds = TaxAssignmentScope.Resolve(request.Scope, request.ProductIds,
                    request.ExpectedProductCount, ownProductIds);
            }
            catch (ArgumentException)
            {
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException)
            {
                return Error(StatusCodes.Status409Conflict, "PRODUCT_SET_CHANGED");
            }

            foreach (var productId in productIds)
                await ProductPriceConsistencyLock.AcquireAsync(db, context.CompanyId,
                    productId, cancellationToken);

            var now = timeProvider.GetUtcNow();
            var changed = tax.Kind == TaxKind.Vat
                ? await AssignVatAsync(db, context.CompanyId, context.UserId, tax,
                    productIds, now, cancellationToken)
                : await AssignOtherAsync(db, context.CompanyId, context.UserId, tax.Id,
                    request.IsAssigned.Value, productIds, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new AssignmentResult(productIds.Length, changed));
        }
        catch (PostgresException exception) when (exception.SqlState is
            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            return Error(StatusCodes.Status409Conflict, "TAX_ASSIGNMENT_CONFLICT");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation or
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        {
            return Error(StatusCodes.Status409Conflict, "TAX_ASSIGNMENT_CONFLICT");
        }
    }

    private static async Task<int> AssignVatAsync(PlatformAccessDbContext db,
        Guid companyId, Guid actorId, TaxCatalogEntry tax, Guid[] productIds,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = await db.ProductTaxRules.Where(item =>
                item.CompanyId == companyId && productIds.Contains(item.ProductId) &&
                item.EffectiveToUtc == null)
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
        var toAdd = new List<Guid>();
        foreach (var productId in productIds)
        {
            if (current.TryGetValue(productId, out var rule))
            {
                if (rule.Treatment == SaleTaxTreatment.Taxed &&
                    rule.RatePercent == tax.RatePercent && rule.TaxCatalogEntryId == tax.Id)
                    continue;
                if (now <= rule.EffectiveFromUtc)
                    now = rule.EffectiveFromUtc.AddTicks(10);
                rule.Close(now);
            }
            toAdd.Add(productId);
        }
        await db.SaveChangesAsync(cancellationToken);
        foreach (var productId in toAdd)
            db.ProductTaxRules.Add(new ProductTaxRule(companyId, productId,
                SaleTaxTreatment.Taxed, tax.RatePercent, actorId, now, tax.Id));
        await db.SaveChangesAsync(cancellationToken);
        return toAdd.Count;
    }

    private static async Task<int> AssignOtherAsync(PlatformAccessDbContext db,
        Guid companyId, Guid actorId, Guid taxId, bool isAssigned,
        Guid[] productIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = await db.OtherTaxAssignments.Where(item =>
                item.CompanyId == companyId && item.TaxCatalogEntryId == taxId &&
                productIds.Contains(item.ProductId) && item.EffectiveToUtc == null)
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
        var changed = 0;
        foreach (var productId in productIds)
        {
            if (isAssigned && current.ContainsKey(productId)) continue;
            if (!isAssigned && !current.ContainsKey(productId)) continue;
            if (isAssigned)
            {
                db.OtherTaxAssignments.Add(new OtherTaxAssignment(companyId,
                    productId, taxId, actorId, now));
            }
            else
            {
                var assignment = current[productId];
                var closedAt = now <= assignment.EffectiveFromUtc
                    ? assignment.EffectiveFromUtc.AddTicks(10) : now;
                assignment.Close(closedAt, actorId);
            }
            changed++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])), statusCode: statusCode);

    private sealed record SetTaxAssignmentRequest(Guid TaxCatalogEntryId, bool? IsAssigned,
        string? Scope, Guid[]? ProductIds, int? ExpectedProductCount);
    private sealed record ProductCount(int Total);
    private sealed record AssignmentResult(int AffectedCount, int ChangedCount);
    private sealed record OtherTaxAssignmentResponse(Guid Id, Guid ProductId,
        Guid TaxCatalogEntryId, string TaxName, decimal RatePercent,
        DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc,
        Guid AssignedByUserId, Guid? RemovedByUserId);
}
