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
        endpoints.MapGet("/api/customers/account-options", AccountOptionsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        endpoints.MapGet("/api/customers/{customerId:guid}/account", AccountAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        return endpoints;
    }

    private static async Task<IResult> AccountAsync(
        Guid customerId, PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken, int page = 1)
    {
        if (customerId == Guid.Empty || page is < 1 or > 10_000)
            return Results.Json(new ErrorResponse(new ApiError("VALIDATION_ERROR",
                "No se pudo completar la solicitud", [])), statusCode: StatusCodes.Status400BadRequest);

        var companyId = accessor.Context.CompanyId;
        var customer = await db.CustomerAccounts.AsNoTracking().Where(item =>
            item.CompanyId == companyId && item.Id == customerId)
            .Select(item => new { item.Id, item.Code, item.Name, item.IsActive, item.CreditEnabled })
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null) return Results.NotFound();

        var charges = db.CustomerSaleCharges.AsNoTracking().Where(charge =>
            charge.CompanyId == companyId && charge.CustomerId == customerId);
        var totalCharges = await charges.SumAsync(charge => (decimal?)charge.Amount, cancellationToken) ?? 0;
        var allocations = db.CustomerCollectionAllocations.AsNoTracking().Where(item =>
            item.CompanyId == companyId && item.CustomerId == customerId);
        var totalAllocated = await allocations.SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
        var creditAvailable = await db.CustomerCollectionReceipts.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.CustomerId == customerId)
            .SumAsync(item => (decimal?)item.CreditAmount, cancellationToken) ?? 0;
        creditAvailable -= await db.CustomerCreditApplications.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.CustomerId == customerId)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
        var count = await charges.CountAsync(cancellationToken);
        var pageCharges = await charges.OrderBy(charge => charge.CreatedAtUtc).ThenBy(charge => charge.Id)
            .Skip((page - 1) * 50).Take(50)
            .Select(charge => new { charge.SaleId, charge.CreatedAtUtc, charge.Amount })
            .ToArrayAsync(cancellationToken);
        var saleIds = pageCharges.Select(item => item.SaleId).ToArray();
        var appliedBySale = await allocations.Where(item => saleIds.Contains(item.SaleId))
            .GroupBy(item => item.SaleId)
            .Select(group => new { SaleId = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.SaleId, item => item.Amount, cancellationToken);
        var sales = pageCharges.Select(charge => new AccountSale(charge.SaleId,
            charge.CreatedAtUtc, charge.Amount, charge.Amount - appliedBySale.GetValueOrDefault(charge.SaleId)))
            .ToArray();
        return Results.Ok(new CustomerAccountResponse(customer.Id, customer.Code, customer.Name,
            customer.IsActive, customer.CreditEnabled, totalCharges - totalAllocated,
            creditAvailable, count, page, sales));
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

    private static async Task<IResult> AccountOptionsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken, string? search = null)
    {
        if ((search?.Length ?? 0) > 100)
            return Results.Json(new ErrorResponse(new ApiError("VALIDATION_ERROR",
                "No se pudo completar la solicitud", [])), statusCode: StatusCodes.Status400BadRequest);

        var query = db.CustomerAccounts.AsNoTracking().Where(customer =>
            customer.CompanyId == accessor.Context.CompanyId);
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
    private sealed record AccountSale(Guid SaleId, DateTimeOffset ChargedAtUtc,
        decimal OriginalAmount, decimal OutstandingAmount);
    private sealed record CustomerAccountResponse(Guid CustomerId, string CustomerCode, string CustomerName,
        bool IsActive, bool CreditEnabled, decimal TotalDebt, decimal CreditAvailable,
        int SaleCount, int Page, IReadOnlyList<AccountSale> Sales);
}
