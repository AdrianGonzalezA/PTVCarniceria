using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Customers;

public static class PosCustomerEndpoints
{
    public static IEndpointRouteBuilder MapPosCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/customers/credit-options", OptionsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        return endpoints;
    }

    private static async Task<IResult> OptionsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken, string? search = null)
    {
        if ((search?.Length ?? 0) > 100)
            return Results.Json(new ErrorResponse(new ApiError("VALIDATION_ERROR",
                "No se pudo completar la solicitud", [])), statusCode: StatusCodes.Status400BadRequest);

        var query = db.CustomerAccounts.AsNoTracking().Where(customer =>
            customer.CompanyId == accessor.Context.CompanyId && customer.IsActive && customer.CreditEnabled);
        var normalized = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalized))
        {
            var pattern = $"%{EscapeLike(normalized)}%";
            query = query.Where(customer => EF.Functions.ILike(customer.Code, pattern, "\\") ||
                EF.Functions.ILike(customer.Name, pattern, "\\"));
        }
        var options = await query.OrderBy(customer => customer.Name).ThenBy(customer => customer.Id)
            .Take(20).Select(customer => new CustomerOption(customer.Id, customer.Code, customer.Name))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(options);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record CustomerOption(Guid Id, string Code, string Name);
}
