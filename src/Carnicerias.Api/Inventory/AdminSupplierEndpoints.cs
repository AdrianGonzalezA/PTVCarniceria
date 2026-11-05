using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class AdminSupplierEndpoints
{
    public static IEndpointRouteBuilder MapAdminSupplierEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var suppliers = endpoints.MapGroup("/api/admin/suppliers")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        suppliers.MapGet("", ListAsync);
        suppliers.MapGet("/options", OptionsAsync);
        suppliers.MapPost("", CreateAsync);
        suppliers.MapPatch("/{supplierId:guid}", UpdateAsync);
        return endpoints;
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
        var query = db.Suppliers.AsNoTracking().Where(supplier => supplier.CompanyId == companyId);
        var normalizedSearch = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
            query = query.Where(supplier =>
                EF.Functions.ILike(supplier.Code, pattern, "\\") ||
                EF.Functions.ILike(supplier.Name, pattern, "\\") ||
                supplier.Identification != null &&
                EF.Functions.ILike(supplier.Identification, pattern, "\\"));
        }

        var totalItems = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(supplier => supplier.Name).ThenBy(supplier => supplier.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(supplier => new SupplierResponse(supplier.Id, supplier.Code, supplier.Name,
                supplier.Identification, supplier.IsActive))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new SupplierPage(items, page, pageSize, totalItems,
            totalItems == 0 ? 0 : (totalItems - 1) / pageSize + 1));
    }

    private static async Task<IResult> OptionsAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var companyId = contextAccessor.Context.CompanyId;
        var items = await db.Suppliers.AsNoTracking()
            .Where(supplier => supplier.CompanyId == companyId && supplier.IsActive)
            .OrderBy(supplier => supplier.Name).ThenBy(supplier => supplier.Id)
            .Select(supplier => new SupplierOption(supplier.Id, supplier.Code, supplier.Name))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        SupplierCreateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (!ValidDetails(request?.Code, request?.Name, request?.Identification))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var supplier = new Supplier(contextAccessor.Context.CompanyId, request!.Code,
            request.Name, request.Identification);
        db.Suppliers.Add(supplier);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "SUPPLIER_ALREADY_EXISTS");
        }

        return Results.Created($"/api/admin/suppliers/{supplier.Id}", ToResponse(supplier));
    }

    private static async Task<IResult> UpdateAsync(
        Guid supplierId,
        SupplierUpdateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (supplierId == Guid.Empty || request is null ||
            (request.Code is null && request.Name is null && request.IsActive is null) ||
            (request.Code is null) != (request.Name is null) ||
            request.Code is not null && !ValidDetails(request.Code, request.Name, request.Identification))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        var supplier = await db.Suppliers.SingleOrDefaultAsync(item =>
            item.Id == supplierId && item.CompanyId == companyId, cancellationToken);
        if (supplier is null) return Error(StatusCodes.Status404NotFound, "SUPPLIER_NOT_FOUND");

        if (request.Code is not null)
            supplier.UpdateDetails(request.Code, request.Name!, request.Identification);
        if (request.IsActive is bool isActive)
        {
            if (isActive) supplier.Activate();
            else supplier.Deactivate();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "SUPPLIER_ALREADY_EXISTS");
        }
        return Results.Ok(ToResponse(supplier));
    }

    private static SupplierResponse ToResponse(Supplier supplier) =>
        new(supplier.Id, supplier.Code, supplier.Name, supplier.Identification, supplier.IsActive);

    private static bool ValidDetails(string? code, string? name, string? identification) =>
        !string.IsNullOrWhiteSpace(code) &&
        code.Trim().Normalize(NormalizationForm.FormKC).Length <= 80 &&
        !string.IsNullOrWhiteSpace(name) &&
        name.Trim().Normalize(NormalizationForm.FormKC).Length <= 200 &&
        (identification?.Trim().Normalize(NormalizationForm.FormKC).Length ?? 0) <= 40;

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

    private sealed record SupplierCreateRequest(string Code, string Name, string? Identification);
    private sealed record SupplierUpdateRequest(string? Code, string? Name,
        string? Identification, bool? IsActive);
    private sealed record SupplierResponse(Guid Id, string Code, string Name,
        string? Identification, bool IsActive);
    private sealed record SupplierOption(Guid Id, string Code, string Name);
    private sealed record SupplierPage(IReadOnlyList<SupplierResponse> Items,
        int Page, int PageSize, long TotalItems, long TotalPages);
}
