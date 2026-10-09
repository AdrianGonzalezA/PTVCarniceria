using System.Data;
using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Catalog;

public static class AdminProductTaxEndpoints
{
    private const int PageSize = 25;

    public static IEndpointRouteBuilder MapAdminProductTaxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/product-tax-rules")
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        group.MapGet("", ListAsync);
        group.MapGet("/{productId:guid}", HistoryAsync);
        group.MapPut("/{productId:guid}", SetAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, string? search = null)
    {
        if (page is < 1 or > 10_000 || (search?.Length ?? 0) > 100)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var companyId = accessor.Context.CompanyId;
        var products = db.CatalogProducts.AsNoTracking().Where(item => item.CompanyId == companyId);
        var normalized = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var pattern = $"%{EscapeLike(normalized)}%";
            products = products.Where(item => EF.Functions.ILike(item.Code, pattern, "\\") ||
                EF.Functions.ILike(item.Name, pattern, "\\"));
        }
        var total = await products.CountAsync(cancellationToken);
        var rows = await products.OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(item => new { item.Id, item.Code, item.Name, item.IsActive })
            .ToArrayAsync(cancellationToken);
        var ids = rows.Select(item => item.Id).ToArray();
        var current = await db.ProductTaxRules.AsNoTracking().Where(item =>
                item.CompanyId == companyId && ids.Contains(item.ProductId) &&
                item.EffectiveToUtc == null)
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
        return Results.Ok(new TaxRulePage(page, PageSize, total,
            rows.Select(item => new TaxProductRow(item.Id, item.Code, item.Name, item.IsActive,
                current.TryGetValue(item.Id, out var rule) ? ToRule(rule) : null)).ToArray()));
    }

    private static async Task<IResult> HistoryAsync(Guid productId, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var companyId = accessor.Context.CompanyId;
        if (productId == Guid.Empty) return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (!await db.CatalogProducts.AsNoTracking().AnyAsync(item =>
                item.CompanyId == companyId && item.Id == productId, cancellationToken))
            return Results.NotFound();
        var rules = await db.ProductTaxRules.AsNoTracking().Where(item =>
                item.CompanyId == companyId && item.ProductId == productId)
            .OrderByDescending(item => item.EffectiveFromUtc).Take(100)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(rules.Select(ToRule).ToArray());
    }

    private static async Task<IResult> SetAsync(Guid productId, SetTaxRuleRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider timeProvider, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (productId == Guid.Empty || request is null ||
            !TryTreatment(request.Treatment, out var treatment) ||
            request.RatePercent is < 0 or > 100 ||
            decimal.Round(request.RatePercent, 2) != request.RatePercent ||
            (treatment != SaleTaxTreatment.Taxed && request.RatePercent != 0))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var context = accessor.Context;
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await ProductPriceConsistencyLock.AcquireAsync(db, context.CompanyId, productId, cancellationToken);
        if (!await db.CatalogProducts.AsNoTracking().AnyAsync(item =>
                item.CompanyId == context.CompanyId && item.Id == productId, cancellationToken))
            return Results.NotFound();
        var current = await db.ProductTaxRules.SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.ProductId == productId &&
            item.EffectiveToUtc == null, cancellationToken);
        if (current is not null && current.Treatment == treatment &&
            current.RatePercent == request.RatePercent)
            return Results.Ok(ToRule(current));
        var now = timeProvider.GetUtcNow();
        if (current is not null)
        {
            if (now <= current.EffectiveFromUtc)
                now = current.EffectiveFromUtc.AddTicks(1);
            current.Close(now);
            await db.SaveChangesAsync(cancellationToken);
        }
        var rule = new ProductTaxRule(context.CompanyId, productId, treatment,
            request.RatePercent, context.UserId, now);
        db.ProductTaxRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(ToRule(rule));
    }

    private static TaxRuleResponse ToRule(ProductTaxRule rule) => new(rule.Id,
        rule.ProductId, TreatmentName(rule.Treatment), rule.RatePercent,
        rule.EffectiveFromUtc, rule.EffectiveToUtc, rule.ChangedByUserId);

    private static string TreatmentName(SaleTaxTreatment treatment) => treatment switch
    {
        SaleTaxTreatment.Taxed => "taxed",
        SaleTaxTreatment.Exempt => "exempt",
        SaleTaxTreatment.NotTaxed => "notTaxed",
        _ => throw new ArgumentOutOfRangeException(nameof(treatment))
    };

    private static bool TryTreatment(string? raw, out SaleTaxTreatment treatment)
    {
        treatment = raw?.Trim().ToLowerInvariant() switch
        {
            "taxed" => SaleTaxTreatment.Taxed,
            "exempt" => SaleTaxTreatment.Exempt,
            "nottaxed" => SaleTaxTreatment.NotTaxed,
            _ => (SaleTaxTreatment)(-1)
        };
        return Enum.IsDefined(treatment);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])), statusCode: statusCode);

    private sealed record SetTaxRuleRequest(string? Treatment, decimal RatePercent);
    private sealed record TaxRuleResponse(Guid Id, Guid ProductId, string Treatment,
        decimal RatePercent, DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc,
        Guid ChangedByUserId);
    private sealed record TaxProductRow(Guid Id, string Code, string Name, bool IsActive,
        TaxRuleResponse? CurrentRule);
    private sealed record TaxRulePage(int Page, int PageSize, int Total,
        IReadOnlyList<TaxProductRow> Items);
}
