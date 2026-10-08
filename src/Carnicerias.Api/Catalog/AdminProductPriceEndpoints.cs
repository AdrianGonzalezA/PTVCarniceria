using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text;

namespace Carnicerias.Api.Catalog;

public static class AdminProductPriceEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductPriceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var prices = endpoints.MapGroup("/api/admin/price-lists/{listId:guid}")
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        prices.MapGet("/prices", ListAsync);
        prices.MapGet("/products/{productId:guid}/history", HistoryAsync);
        prices.MapPut("/products/{productId:guid}/price", SetPriceAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid listId,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? search = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (search?.Length ?? 0) > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var companyId = contextAccessor.Context.CompanyId;
        if (!await ListExistsAsync(db, companyId, listId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PRICE_LIST_NOT_FOUND");

        var products = db.CatalogProducts.AsNoTracking().Where(product => product.CompanyId == companyId);
        var normalizedSearch = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
            products = products.Where(product => EF.Functions.ILike(product.Name, pattern, "\\") ||
                EF.Functions.ILike(product.Code, pattern, "\\"));
        }

        var totalItems = await products.LongCountAsync(cancellationToken);
        var items = await products.OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(product => new ProductPriceRow(
                product.Id, product.Code, product.Name, product.Unit, product.Cost, product.IsActive,
                db.ProductPrices.Where(price => price.CompanyId == companyId &&
                        price.PriceListId == listId && price.ProductId == product.Id &&
                        price.EffectiveToUtc == null)
                    .Select(price => (decimal?)price.Amount).FirstOrDefault(),
                db.ProductPrices.Where(price => price.CompanyId == companyId &&
                        price.PriceListId == listId && price.ProductId == product.Id &&
                        price.EffectiveToUtc == null)
                    .Select(price => (DateTimeOffset?)price.EffectiveFromUtc).FirstOrDefault()))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new ProductPricePage(items, page, pageSize, totalItems,
            totalItems == 0 ? 0 : (totalItems - 1) / pageSize + 1));
    }

    private static async Task<IResult> HistoryAsync(
        Guid listId,
        Guid productId,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var companyId = contextAccessor.Context.CompanyId;
        if (!await ListExistsAsync(db, companyId, listId, cancellationToken) ||
            !await ProductExistsAsync(db, companyId, productId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PRICE_LIST_OR_PRODUCT_NOT_FOUND");

        var history = await db.ProductPrices.AsNoTracking()
            .Where(price => price.CompanyId == companyId && price.PriceListId == listId &&
                price.ProductId == productId)
            .OrderByDescending(price => price.EffectiveFromUtc)
            .Select(price => new PriceHistoryRow(price.Id, price.Amount, price.EffectiveFromUtc,
                price.EffectiveToUtc,
                db.Users.Where(user => user.Id == price.ChangedByUserId)
                    .Select(user => user.Username).First()))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(history);
    }

    private static async Task<IResult> SetPriceAsync(
        Guid listId,
        Guid productId,
        PriceChangeRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || !ValidAmount(request.Amount))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = contextAccessor.Context;
        if (!await ListExistsAsync(db, context.CompanyId, listId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PRICE_LIST_NOT_FOUND");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProductPriceConsistencyLock.AcquireAsync(db, context.CompanyId, productId, cancellationToken);
        var product = await db.CatalogProducts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.Id == productId, cancellationToken);
        if (product is null) return Error(StatusCodes.Status404NotFound, "PRODUCT_NOT_FOUND");
        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount < product.Cost)
            return Error(StatusCodes.Status409Conflict, "PRICE_BELOW_COST");

        var current = await db.ProductPrices.SingleOrDefaultAsync(price =>
            price.CompanyId == context.CompanyId && price.PriceListId == listId &&
            price.ProductId == productId && price.EffectiveToUtc == null, cancellationToken);
        if (current is not null && current.Amount == amount)
            return Results.Ok(new CurrentPriceResponse(current.Id, current.Amount, current.EffectiveFromUtc));

        var now = timeProvider.GetUtcNow();
        if (current is not null)
        {
            if (now <= current.EffectiveFromUtc)
                return Error(StatusCodes.Status409Conflict, "PRICE_NOT_YET_EFFECTIVE");
            current.CloseAt(now);
            await db.SaveChangesAsync(cancellationToken);
        }

        var next = new ProductPrice(context.CompanyId, listId, productId, amount, now, context.UserId);
        db.ProductPrices.Add(next);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Error(StatusCodes.Status409Conflict, "PRICE_CHANGE_CONFLICT");
        }
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new CurrentPriceResponse(next.Id, next.Amount, next.EffectiveFromUtc));
    }

    private static Task<bool> ListExistsAsync(
        PlatformAccessDbContext db, Guid companyId, Guid listId, CancellationToken cancellationToken) =>
        db.PriceLists.AnyAsync(list => list.CompanyId == companyId && list.Id == listId, cancellationToken);

    private static Task<bool> ProductExistsAsync(
        PlatformAccessDbContext db, Guid companyId, Guid productId, CancellationToken cancellationToken) =>
        db.CatalogProducts.AnyAsync(product => product.CompanyId == companyId && product.Id == productId,
            cancellationToken);

    private static bool ValidAmount(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        return rounded > 0 && rounded <= 9_999_999_999.99m;
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record PriceChangeRequest(decimal Amount);
    private sealed record ProductPriceRow(Guid ProductId, string Code, string Name, string Unit,
        decimal Cost, bool IsActive, decimal? CurrentPrice, DateTimeOffset? EffectiveFromUtc);
    private sealed record ProductPricePage(IReadOnlyList<ProductPriceRow> Items, int Page, int PageSize,
        long TotalItems, long TotalPages);
    private sealed record PriceHistoryRow(Guid Id, decimal Amount, DateTimeOffset EffectiveFromUtc,
        DateTimeOffset? EffectiveToUtc, string ChangedByUsername);
    private sealed record CurrentPriceResponse(Guid Id, decimal Amount, DateTimeOffset EffectiveFromUtc);
}
