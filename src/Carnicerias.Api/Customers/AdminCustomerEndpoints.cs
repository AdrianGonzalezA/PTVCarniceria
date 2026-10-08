using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Customers;

public static class AdminCustomerEndpoints
{
    public static IEndpointRouteBuilder MapAdminCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customers = endpoints.MapGroup("/api/admin/customers")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        customers.MapGet("", ListAsync);
        customers.MapPost("", CreateAsync);
        customers.MapPatch("/{customerId:guid}", UpdateAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken, int page = 1, int pageSize = 20, string? search = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (search?.Length ?? 0) > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var query = db.CustomerAccounts.AsNoTracking()
            .Where(customer => customer.CompanyId == accessor.Context.CompanyId);
        var normalizedSearch = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var pattern = $"%{EscapeLike(normalizedSearch)}%";
            query = query.Where(customer => EF.Functions.ILike(customer.Name, pattern, "\\") ||
                EF.Functions.ILike(customer.Code, pattern, "\\"));
        }

        var totalItems = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(customer => customer.Name).ThenBy(customer => customer.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(customer => new CustomerResponse(customer.Id, customer.Code, customer.Name,
                customer.IsActive, customer.CreditEnabled))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new CustomerPage(items, page, pageSize, totalItems,
            totalItems == 0 ? 0 : (totalItems - 1) / pageSize + 1));
    }

    private static async Task<IResult> CreateAsync(
        CustomerCreateRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || !ValidDetails(request.Code, request.Name))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var customer = new CustomerAccount(accessor.Context.CompanyId, request.Code, request.Name);
        db.CustomerAccounts.Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "CUSTOMER_CODE_EXISTS");
        }
        return Results.Created($"/api/admin/customers/{customer.Id}", ToResponse(customer));
    }

    private static async Task<IResult> UpdateAsync(
        Guid customerId, CustomerUpdateRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (customerId == Guid.Empty || request is null ||
            (request.Code is null && request.Name is null && request.IsActive is null && request.CreditEnabled is null) ||
            (request.Code is not null && !ValidCode(request.Code)) ||
            (request.Name is not null && !ValidName(request.Name)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var customer = await db.CustomerAccounts.SingleOrDefaultAsync(item =>
            item.CompanyId == accessor.Context.CompanyId && item.Id == customerId, cancellationToken);
        if (customer is null) return Error(StatusCodes.Status404NotFound, "CUSTOMER_NOT_FOUND");

        if (request.Code is not null || request.Name is not null)
            customer.UpdateDetails(request.Code ?? customer.Code, request.Name ?? customer.Name);
        if (request.IsActive is bool active)
        {
            if (active) customer.Activate();
            else customer.Deactivate();
        }
        if (request.CreditEnabled is bool creditEnabled)
        {
            if (creditEnabled && !customer.IsActive)
                return Error(StatusCodes.Status409Conflict, "CUSTOMER_INACTIVE");
            if (creditEnabled) customer.EnableCredit();
            else customer.DisableCredit();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "CUSTOMER_CODE_EXISTS");
        }
        return Results.Ok(ToResponse(customer));
    }

    private static CustomerResponse ToResponse(CustomerAccount customer) =>
        new(customer.Id, customer.Code, customer.Name, customer.IsActive, customer.CreditEnabled);

    private static bool ValidDetails(string? code, string? name) => ValidCode(code) && ValidName(name);
    private static bool ValidCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Trim().Normalize(NormalizationForm.FormKC).Length <= 80;
    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Normalize(NormalizationForm.FormKC).Length <= 200;
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])), statusCode: statusCode);

    private sealed record CustomerCreateRequest(string Code, string Name);
    private sealed record CustomerUpdateRequest(string? Code, string? Name, bool? IsActive, bool? CreditEnabled);
    private sealed record CustomerResponse(Guid Id, string Code, string Name, bool IsActive, bool CreditEnabled);
    private sealed record CustomerPage(IReadOnlyList<CustomerResponse> Items, int Page, int PageSize,
        long TotalItems, long TotalPages);
}
