using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Catalog;

public static class AdminProductCodeEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductCodeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var codes = endpoints.MapGroup("/api/admin/products/{productId:guid}/codes")
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        codes.MapGet("", ListAsync);
        codes.MapPost("", CreateAsync);
        codes.MapPatch("", ChangeStateAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid productId,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var companyId = contextAccessor.Context.CompanyId;
        if (!await ProductExistsAsync(db, companyId, productId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PRODUCT_NOT_FOUND");

        var codes = await db.ProductCodes.AsNoTracking()
            .Where(code => code.CompanyId == companyId && code.ProductId == productId)
            .OrderBy(code => code.Code)
            .Select(code => new ProductCodeResponse(code.Code, code.IsActive))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(codes);
    }

    private static async Task<IResult> CreateAsync(
        Guid productId,
        ProductCodeCreateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 80)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        if (!await ProductExistsAsync(db, companyId, productId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PRODUCT_NOT_FOUND");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProductCodeReservation.AcquireCompanyLockAsync(db, companyId, cancellationToken);
        var normalized = request.Code.Trim().ToUpperInvariant();
        if (await db.CatalogProducts.AnyAsync(product => product.CompanyId == companyId &&
                product.NormalizedCode == normalized, cancellationToken) ||
            await db.ProductCodes.AnyAsync(code => code.CompanyId == companyId &&
                code.NormalizedCode == normalized, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "PRODUCT_CODE_ALREADY_EXISTS");

        var alternate = new ProductCode(companyId, productId, request.Code);
        db.ProductCodes.Add(alternate);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Error(StatusCodes.Status409Conflict, "PRODUCT_CODE_ALREADY_EXISTS");
        }
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/admin/products/{productId}/codes", new ProductCodeResponse(alternate.Code, true));
    }

    private static async Task<IResult> ChangeStateAsync(
        Guid productId,
        ProductCodeStateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 80)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        if (!await ProductExistsAsync(db, companyId, productId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PRODUCT_NOT_FOUND");
        var normalized = request.Code.Trim().ToUpperInvariant();
        var code = await db.ProductCodes.SingleOrDefaultAsync(item => item.CompanyId == companyId &&
            item.ProductId == productId && item.NormalizedCode == normalized, cancellationToken);
        if (code is null) return Error(StatusCodes.Status404NotFound, "PRODUCT_CODE_NOT_FOUND");

        if (request.IsActive) code.Activate();
        else code.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ProductCodeResponse(code.Code, code.IsActive));
    }

    private static Task<bool> ProductExistsAsync(
        PlatformAccessDbContext db, Guid companyId, Guid productId, CancellationToken cancellationToken) =>
        db.CatalogProducts.AnyAsync(product => product.CompanyId == companyId && product.Id == productId,
            cancellationToken);

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record ProductCodeCreateRequest(string Code);
    private sealed record ProductCodeStateRequest(string Code, bool IsActive);
    private sealed record ProductCodeResponse(string Code, bool IsActive);
}
