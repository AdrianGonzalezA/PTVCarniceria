using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text;

namespace Carnicerias.Api.Catalog;

public static class AdminProductEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var products = endpoints.MapGroup("/api/admin/products")
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        products.MapGet("", ListAsync);
        products.MapPost("", CreateAsync);
        products.MapPatch("/{productId:guid}", UpdateAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? search = null,
        Guid? categoryId = null,
        bool? isActive = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (search?.Length ?? 0) > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        var products = db.CatalogProducts.AsNoTracking()
            .Where(product => product.CompanyId == companyId &&
                (!categoryId.HasValue || product.CategoryId == categoryId.Value) &&
                (!isActive.HasValue || product.IsActive == isActive.Value));
        var normalizedSearch = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
            products = products.Where(product =>
                EF.Functions.ILike(product.Name, pattern, "\\") ||
                EF.Functions.ILike(product.Code, pattern, "\\") ||
                db.ProductCodes.Any(code => code.CompanyId == companyId &&
                    code.ProductId == product.Id && EF.Functions.ILike(code.Code, pattern, "\\")));
        }

        var totalItems = await products.LongCountAsync(cancellationToken);
        var items = await products
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductResponse(
                product.Id,
                product.CategoryId,
                db.ProductCategories.Where(category => category.Id == product.CategoryId &&
                        category.CompanyId == companyId)
                    .Select(category => category.Name).First(),
                product.Code,
                product.Name,
                product.Unit,
                product.SaleMode == ProductSaleMode.Weight ? "weight" : "unit",
                product.Cost,
                product.IsActive,
                db.ProductCodes.Count(code => code.CompanyId == companyId &&
                    code.ProductId == product.Id && code.IsActive)))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new ProductPage(items, page, pageSize, totalItems,
            totalItems == 0 ? 0 : (totalItems - 1) / pageSize + 1));
    }

    private static async Task<IResult> CreateAsync(
        ProductCreateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || request.CategoryId == Guid.Empty ||
            !ValidText(request.Code, 80) || !ValidText(request.Name, 200) ||
            !ValidText(request.Unit, 24) || !TryParseSaleMode(request.SaleMode, out var saleMode) ||
            !ValidCost(request.Cost))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        var category = await db.ProductCategories.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == request.CategoryId && item.IsActive, cancellationToken);
        if (category is null) return Error(StatusCodes.Status400BadRequest, "CATEGORY_NOT_ACTIVE");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProductCodeReservation.AcquireCompanyLockAsync(db, companyId, cancellationToken);
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        if (await CodeExistsAsync(db, companyId, normalizedCode, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "PRODUCT_CODE_ALREADY_EXISTS");

        var product = new CatalogProduct(companyId, category.Id, request.Code,
            request.Name, request.Unit, saleMode, request.Cost);
        db.CatalogProducts.Add(product);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "PRODUCT_CODE_ALREADY_EXISTS");
        }
        await transaction.CommitAsync(cancellationToken);

        return Results.Created($"/api/admin/products/{product.Id}", ToResponse(product, category.Name, 0));
    }

    private static async Task<IResult> UpdateAsync(
        Guid productId,
        ProductUpdateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (productId == Guid.Empty || request is null || request.Code is not null ||
            (request.CategoryId is null && request.Name is null && request.Unit is null &&
             request.SaleMode is null && request.Cost is null && request.IsActive is null) ||
            (request.CategoryId.HasValue && request.CategoryId == Guid.Empty) ||
            (request.Name is not null && !ValidText(request.Name, 200)) ||
            (request.Unit is not null && !ValidText(request.Unit, 24)) ||
            (request.Cost.HasValue && !ValidCost(request.Cost.Value)) ||
            (request.SaleMode is not null && !TryParseSaleMode(request.SaleMode, out _)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProductPriceConsistencyLock.AcquireAsync(db, companyId, productId, cancellationToken);
        var product = await db.CatalogProducts.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == productId, cancellationToken);
        if (product is null) return Error(StatusCodes.Status404NotFound, "PRODUCT_NOT_FOUND");

        var nextCategoryId = request.CategoryId ?? product.CategoryId;
        var category = await db.ProductCategories.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == nextCategoryId, cancellationToken);
        if (category is null) return Error(StatusCodes.Status400BadRequest, "CATEGORY_NOT_FOUND");
        if (!category.IsActive &&
            ((request.CategoryId != null && nextCategoryId != product.CategoryId) || request.IsActive == true))
            return Error(StatusCodes.Status409Conflict, "CATEGORY_NOT_ACTIVE");

        var nextMode = request.SaleMode is null
            ? product.SaleMode
            : ParseSaleMode(request.SaleMode);
        var nextUnit = request.Unit ?? product.Unit;
        if ((nextUnit.Trim() != product.Unit || nextMode != product.SaleMode) &&
            await HasQuantityHistoryAsync(db, companyId, productId, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "PRODUCT_QUANTITY_HISTORY_EXISTS");
        if (request.Cost is decimal requestedCost)
        {
            var roundedCost = decimal.Round(requestedCost, 2, MidpointRounding.AwayFromZero);
            if (await db.ProductPrices.AnyAsync(price => price.CompanyId == companyId &&
                price.ProductId == productId && price.EffectiveToUtc == null &&
                price.Amount < roundedCost, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "COST_ABOVE_CURRENT_PRICE");
        }

        if (request.CategoryId is not null || request.Name is not null || request.Unit is not null ||
            request.SaleMode is not null || request.Cost is not null)
            product.UpdateDetails(nextCategoryId, request.Name ?? product.Name, nextUnit,
                nextMode, request.Cost ?? product.Cost);
        if (request.IsActive is bool isActive)
        {
            if (isActive) product.Activate();
            else product.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var alternateCodeCount = await db.ProductCodes.CountAsync(code =>
            code.CompanyId == companyId && code.ProductId == productId && code.IsActive, cancellationToken);
        return Results.Ok(ToResponse(product, category.Name, alternateCodeCount));
    }

    private static async Task<bool> HasQuantityHistoryAsync(
        PlatformAccessDbContext db, Guid companyId, Guid productId, CancellationToken cancellationToken) =>
        await db.BranchInventoryBalances.AnyAsync(balance => balance.CompanyId == companyId &&
            balance.ProductId == productId && (balance.OnHand != 0 || balance.Reserved != 0), cancellationToken) ||
        await db.InventoryMovements.AnyAsync(movement => movement.CompanyId == companyId &&
            movement.ProductId == productId, cancellationToken) ||
        await db.SaleDrafts.SelectMany(draft => draft.Lines).AnyAsync(line =>
            line.CompanyId == companyId && line.ProductId == productId, cancellationToken) ||
        await db.ConfirmedSaleLines.AnyAsync(line => line.CompanyId == companyId &&
            line.ProductId == productId, cancellationToken);

    private static async Task<bool> CodeExistsAsync(
        PlatformAccessDbContext db, Guid companyId, string normalizedCode, CancellationToken cancellationToken) =>
        await db.CatalogProducts.AnyAsync(product => product.CompanyId == companyId &&
            product.NormalizedCode == normalizedCode, cancellationToken) ||
        await db.ProductCodes.AnyAsync(code => code.CompanyId == companyId &&
            code.NormalizedCode == normalizedCode, cancellationToken);

    private static ProductResponse ToResponse(CatalogProduct product, string categoryName, int alternateCodeCount) =>
        new(product.Id, product.CategoryId, categoryName, product.Code, product.Name, product.Unit,
            product.SaleMode == ProductSaleMode.Weight ? "weight" : "unit", product.Cost,
            product.IsActive, alternateCodeCount);

    private static bool ValidText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength;

    private static bool ValidCost(decimal cost)
    {
        var rounded = decimal.Round(cost, 2, MidpointRounding.AwayFromZero);
        return rounded > 0 && rounded <= 9_999_999_999.99m;
    }

    private static bool TryParseSaleMode(string? value, out ProductSaleMode mode)
    {
        mode = value switch
        {
            "weight" => ProductSaleMode.Weight,
            "unit" => ProductSaleMode.Unit,
            _ => (ProductSaleMode)(-1)
        };
        return Enum.IsDefined(mode);
    }

    private static ProductSaleMode ParseSaleMode(string value) =>
        value == "weight" ? ProductSaleMode.Weight : ProductSaleMode.Unit;

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record ProductCreateRequest(
        Guid CategoryId, string Code, string Name, string Unit, string SaleMode, decimal Cost);
    private sealed record ProductUpdateRequest(
        Guid? CategoryId, string? Name, string? Unit, string? SaleMode, decimal? Cost,
        bool? IsActive, string? Code);
    private sealed record ProductResponse(
        Guid Id, Guid CategoryId, string CategoryName, string Code, string Name, string Unit,
        string SaleMode, decimal Cost, bool IsActive, int AlternateCodeCount);
    private sealed record ProductPage(
        IReadOnlyList<ProductResponse> Items, int Page, int PageSize, long TotalItems, long TotalPages);
}
