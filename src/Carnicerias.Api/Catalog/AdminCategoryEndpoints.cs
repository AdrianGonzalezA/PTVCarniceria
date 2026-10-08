using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text;

namespace Carnicerias.Api.Catalog;

public static class AdminCategoryEndpoints
{
    public static IEndpointRouteBuilder MapAdminCategoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var categories = endpoints.MapGroup("/api/admin/categories")
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        categories.MapGet("", ListAsync);
        categories.MapGet("/options", OptionsAsync);
        categories.MapPost("", CreateAsync);
        categories.MapPatch("/{categoryId:guid}", UpdateAsync);
        return endpoints;
    }

    private static async Task<IResult> OptionsAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var companyId = contextAccessor.Context.CompanyId;
        var options = await db.ProductCategories.AsNoTracking()
            .Where(category => category.CompanyId == companyId && category.IsActive)
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new CategoryOption(category.Id, category.Name))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(options);
    }

    private static async Task<IResult> ListAsync(
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
        var categories = db.ProductCategories.AsNoTracking()
            .Where(category => category.CompanyId == companyId);
        var normalizedSearch = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
            categories = categories.Where(category => EF.Functions.ILike(category.Name, pattern, "\\"));
        }

        var totalItems = await categories.LongCountAsync(cancellationToken);
        var items = await categories
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(category => new CategoryResponse(
                category.Id,
                category.Name,
                category.IsActive,
                db.CatalogProducts.Count(product => product.CompanyId == companyId &&
                    product.CategoryId == category.Id)))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new CategoryPage(items, page, pageSize, totalItems,
            totalItems == 0 ? 0 : (totalItems - 1) / pageSize + 1));
    }

    private static async Task<IResult> CreateAsync(
        CategoryCreateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || !ValidName(request.Name))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var category = new ProductCategory(contextAccessor.Context.CompanyId, request.Name);
        db.ProductCategories.Add(category);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "CATEGORY_ALREADY_EXISTS");
        }

        return Results.Created($"/api/admin/categories/{category.Id}",
            new CategoryResponse(category.Id, category.Name, category.IsActive, 0));
    }

    private static async Task<IResult> UpdateAsync(
        Guid categoryId,
        CategoryUpdateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (categoryId == Guid.Empty || request is null ||
            (request.Name is null && request.IsActive is null) ||
            (request.Name is not null && !ValidName(request.Name)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        var category = await db.ProductCategories.SingleOrDefaultAsync(item =>
            item.Id == categoryId && item.CompanyId == companyId, cancellationToken);
        if (category is null) return Error(StatusCodes.Status404NotFound, "CATEGORY_NOT_FOUND");

        if (request.Name is not null) category.Rename(request.Name);
        if (request.IsActive is bool isActive)
        {
            if (isActive) category.Activate();
            else category.Deactivate();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "CATEGORY_ALREADY_EXISTS");
        }

        var productCount = await db.CatalogProducts.CountAsync(product =>
            product.CompanyId == companyId && product.CategoryId == category.Id, cancellationToken);
        return Results.Ok(new CategoryResponse(category.Id, category.Name, category.IsActive, productCount));
    }

    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120;

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

    private sealed record CategoryCreateRequest(string Name);
    private sealed record CategoryUpdateRequest(string? Name, bool? IsActive);
    private sealed record CategoryResponse(Guid Id, string Name, bool IsActive, int ProductCount);
    private sealed record CategoryOption(Guid Id, string Name);
    private sealed record CategoryPage(
        IReadOnlyList<CategoryResponse> Items,
        int Page,
        int PageSize,
        long TotalItems,
        long TotalPages);
}
