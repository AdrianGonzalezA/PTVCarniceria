using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Catalog;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/catalog/price-lists", GetPriceListsAsync)
            .RequireOperationalContext();
        endpoints.MapGet("/api/catalog/categories", GetCategoriesAsync)
            .RequireOperationalContext();
        endpoints.MapGet("/api/catalog/products", GetProductsAsync)
            .RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> GetCategoriesAsync(
        Guid? priceListId,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (priceListId is null || priceListId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = contextAccessor.Context;
        if (!await IsPriceListAvailableAsync(db, context.CompanyId, context.BranchId, priceListId.Value, cancellationToken))
            return Error(StatusCodes.Status403Forbidden, "PRICE_LIST_NOT_AVAILABLE");

        var now = timeProvider.GetUtcNow();
        var categories = await db.ProductCategories.AsNoTracking()
            .Where(category => category.CompanyId == context.CompanyId && category.IsActive &&
                db.CatalogProducts.Any(product => product.CompanyId == context.CompanyId &&
                    product.CategoryId == category.Id && product.IsActive &&
                    db.ProductPrices.Any(price => price.CompanyId == context.CompanyId &&
                        price.PriceListId == priceListId && price.ProductId == product.Id &&
                        price.EffectiveFromUtc <= now &&
                        (price.EffectiveToUtc == null || price.EffectiveToUtc > now))))
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new CategoryOption(
                category.Id,
                category.Name,
                db.CatalogProducts.Count(product => product.CompanyId == context.CompanyId &&
                    product.CategoryId == category.Id && product.IsActive &&
                    db.ProductPrices.Any(price => price.CompanyId == context.CompanyId &&
                        price.PriceListId == priceListId && price.ProductId == product.Id &&
                        price.EffectiveFromUtc <= now &&
                        (price.EffectiveToUtc == null || price.EffectiveToUtc > now)))))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(categories);
    }

    private static async Task<IResult> GetProductsAsync(
        Guid? priceListId,
        Guid? categoryId,
        string? q,
        string? code,
        int? page,
        int? pageSize,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var query = q?.Trim();
        var exactCode = code?.Trim().ToUpperInvariant();
        var pageNumber = page ?? 1;
        var pageLength = pageSize ?? 50;
        if (priceListId is null || priceListId == Guid.Empty || pageNumber < 1 || pageLength is < 1 or > 100 ||
            (!string.IsNullOrEmpty(query) && query.Length < 3) ||
            (exactCode is not null && exactCode.Length is < 1 or > 80))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = contextAccessor.Context;
        if (!await IsPriceListAvailableAsync(db, context.CompanyId, context.BranchId, priceListId.Value, cancellationToken))
            return Error(StatusCodes.Status403Forbidden, "PRICE_LIST_NOT_AVAILABLE");

        var now = timeProvider.GetUtcNow();
        var products = db.CatalogProducts.AsNoTracking()
            .Where(product => product.CompanyId == context.CompanyId && product.IsActive &&
                db.ProductCategories.Any(category => category.CompanyId == context.CompanyId &&
                    category.Id == product.CategoryId && category.IsActive) &&
                (!categoryId.HasValue || product.CategoryId == categoryId.Value) &&
                db.ProductPrices.Any(price => price.CompanyId == context.CompanyId &&
                    price.PriceListId == priceListId && price.ProductId == product.Id &&
                    price.EffectiveFromUtc <= now &&
                    (price.EffectiveToUtc == null || price.EffectiveToUtc > now)));

        if (exactCode is not null)
        {
            products = products.Where(product => product.NormalizedCode == exactCode ||
                db.ProductCodes.Any(alternate => alternate.CompanyId == context.CompanyId &&
                    alternate.ProductId == product.Id && alternate.IsActive &&
                    alternate.NormalizedCode == exactCode));
        }
        else if (!string.IsNullOrEmpty(query))
        {
            var pattern = $"%{query}%";
            products = products.Where(product => EF.Functions.ILike(product.Name, pattern) ||
                EF.Functions.ILike(product.Code, pattern) ||
                db.ProductCodes.Any(alternate => alternate.CompanyId == context.CompanyId &&
                    alternate.ProductId == product.Id && alternate.IsActive &&
                    EF.Functions.ILike(alternate.Code, pattern)));
        }

        var totalItems = await products.CountAsync(cancellationToken);
        var items = await products
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip((pageNumber - 1) * pageLength)
            .Take(pageLength)
            .Select(product => new ProductOption(
                product.Id,
                product.Code,
                product.Name,
                product.CategoryId,
                product.Unit,
                product.SaleMode == ProductSaleMode.Weight ? "weight" : "unit",
                db.ProductPrices.Where(price => price.CompanyId == context.CompanyId &&
                        price.PriceListId == priceListId && price.ProductId == product.Id &&
                        price.EffectiveFromUtc <= now &&
                        (price.EffectiveToUtc == null || price.EffectiveToUtc > now))
                    .OrderByDescending(price => price.EffectiveFromUtc)
                    .Select(price => price.Amount)
                    .First(),
                "ARS",
                db.ProductPrices.Where(price => price.CompanyId == context.CompanyId &&
                        price.PriceListId == priceListId && price.ProductId == product.Id &&
                        price.EffectiveFromUtc <= now &&
                        (price.EffectiveToUtc == null || price.EffectiveToUtc > now))
                    .OrderByDescending(price => price.EffectiveFromUtc)
                    .Select(price => price.EffectiveFromUtc)
                    .First(),
                db.BranchInventoryBalances.Where(balance => balance.CompanyId == context.CompanyId &&
                        balance.BranchId == context.BranchId && balance.ProductId == product.Id)
                    .Select(balance => balance.OnHand - balance.Reserved)
                    .FirstOrDefault()))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new ProductPage(items, pageNumber, pageLength, totalItems));
    }

    private static Task<bool> IsPriceListAvailableAsync(
        PlatformAccessDbContext db,
        Guid companyId,
        Guid branchId,
        Guid priceListId,
        CancellationToken cancellationToken) =>
        (from assignment in db.BranchPriceLists.AsNoTracking()
         join list in db.PriceLists.AsNoTracking() on assignment.PriceListId equals list.Id
         where assignment.CompanyId == companyId && assignment.BranchId == branchId &&
               assignment.PriceListId == priceListId && assignment.IsActive &&
               list.CompanyId == companyId && list.IsActive
         select assignment.BranchId).AnyAsync(cancellationToken);

    private static async Task<IResult> GetPriceListsAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var context = contextAccessor.Context;
        var lists = await (
            from assignment in db.BranchPriceLists.AsNoTracking()
            join list in db.PriceLists.AsNoTracking()
                on assignment.PriceListId equals list.Id
            where assignment.CompanyId == context.CompanyId &&
                  assignment.BranchId == context.BranchId &&
                  assignment.IsActive &&
                  list.CompanyId == context.CompanyId &&
                  list.IsActive
            orderby list.Name, list.Id
            select new PriceListOption(list.Id, list.Name))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(lists);
    }

    private sealed record PriceListOption(Guid Id, string Name);

    private sealed record CategoryOption(Guid Id, string Name, int ProductCount);

    private sealed record ProductOption(
        Guid Id,
        string Code,
        string Name,
        Guid CategoryId,
        string Unit,
        string SaleMode,
        decimal Price,
        string Currency,
        DateTimeOffset PriceEffectiveFromUtc,
        decimal AvailableStock);

    private sealed record ProductPage(
        IReadOnlyList<ProductOption> Items,
        int Page,
        int PageSize,
        int TotalItems);

    private static IResult Error(int statusCode, string code) => Results.Json(
        new Carnicerias.Api.Contracts.ErrorResponse(
            new Carnicerias.Api.Contracts.ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);
}
