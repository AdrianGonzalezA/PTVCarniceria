using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Customers;

public static class AdminAccountEndpoints
{
    private const int PageSize = 25;

    public static IEndpointRouteBuilder MapAdminAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var accounts = endpoints.MapGroup("/api/admin/accounts")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        accounts.MapGet("", ListAsync);
        accounts.MapGet("/{customerId:guid}", DetailAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, string? search = null)
    {
        if (page is < 1 or > 10_000 || (search?.Length ?? 0) > 100)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var companyId = accessor.Context.CompanyId;
        var query = db.CustomerAccounts.AsNoTracking().Where(item => item.CompanyId == companyId);
        var normalized = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalized))
        {
            var pattern = $"%{EscapeLike(normalized)}%";
            query = query.Where(item => EF.Functions.ILike(item.Code, pattern, "\\") ||
                EF.Functions.ILike(item.Name, pattern, "\\"));
        }
        var total = await query.CountAsync(cancellationToken);
        var customers = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(item => new { item.Id, item.Code, item.Name, item.IsActive, item.CreditEnabled })
            .ToArrayAsync(cancellationToken);
        var balances = await GetBalancesAsync(db, companyId, customers.Select(item => item.Id).ToArray(),
            cancellationToken);
        return Results.Ok(new AccountListResponse(page, PageSize, total,
            customers.Select(item =>
            {
                var balance = balances.GetValueOrDefault(item.Id);
                return new CustomerRow(item.Id, item.Code, item.Name, item.IsActive,
                    item.CreditEnabled, balance.Debt, balance.Credit);
            }).ToArray()));
    }

    private static async Task<IResult> DetailAsync(Guid customerId, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int salePage = 1, int receiptPage = 1, int applicationPage = 1)
    {
        if (customerId == Guid.Empty || salePage is < 1 or > 10_000 ||
            receiptPage is < 1 or > 10_000 || applicationPage is < 1 or > 10_000)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var companyId = accessor.Context.CompanyId;
        var customer = await db.CustomerAccounts.AsNoTracking().Where(item =>
                item.CompanyId == companyId && item.Id == customerId)
            .Select(item => new { item.Id, item.Code, item.Name, item.IsActive, item.CreditEnabled })
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null) return Results.NotFound();
        var balance = (await GetBalancesAsync(db, companyId, [customerId], cancellationToken))
            .GetValueOrDefault(customerId);

        var charges = db.CustomerSaleCharges.AsNoTracking().Where(item =>
            item.CompanyId == companyId && item.CustomerId == customerId);
        var saleCount = await charges.CountAsync(cancellationToken);
        var saleRows = await charges.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((salePage - 1) * PageSize).Take(PageSize)
            .Select(item => new { item.SaleId, item.BranchId, item.CashierShiftId,
                item.CreatedAtUtc, item.Amount })
            .ToArrayAsync(cancellationToken);
        var saleIds = saleRows.Select(item => item.SaleId).ToArray();
        var applied = await db.CustomerCollectionAllocations.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.CustomerId == customerId &&
                saleIds.Contains(item.SaleId) && !item.Receipt.IsVoided)
            .GroupBy(item => item.SaleId)
            .Select(group => new { SaleId = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.SaleId, item => item.Amount, cancellationToken);

        var receipts = db.CustomerCollectionReceipts.AsNoTracking().Where(item =>
            item.CompanyId == companyId && item.CustomerId == customerId);
        var receiptCount = await receipts.CountAsync(cancellationToken);
        var receiptRows = await receipts.Include(item => item.Allocations)
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((receiptPage - 1) * PageSize).Take(PageSize)
            .ToArrayAsync(cancellationToken);
        var receiptIds = receiptRows.Select(item => item.Id).ToArray();
        var corrections = await db.CustomerCollectionCorrections.AsNoTracking()
            .Where(item => item.CompanyId == companyId && receiptIds.Contains(item.OriginalReceiptId))
            .Select(item => new { item.OriginalReceiptId, item.CorrectionNumber,
                item.Kind, item.Reason, item.ReplacementReceiptId, item.CreatedAtUtc })
            .ToDictionaryAsync(item => item.OriginalReceiptId, cancellationToken);

        var applications = db.CustomerCreditApplications.AsNoTracking().Where(item =>
            item.CompanyId == companyId && item.CustomerId == customerId);
        var applicationCount = await applications.CountAsync(cancellationToken);
        var applicationRows = await applications.OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((applicationPage - 1) * PageSize).Take(PageSize)
            .Select(item => new CreditApplicationRow(item.Id, item.SaleId, item.BranchId,
                item.CashierShiftId, item.Amount, item.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new AccountDetailResponse(customer.Id, customer.Code, customer.Name,
            customer.IsActive, customer.CreditEnabled, balance.Debt, balance.Credit,
            new AccountPage<ChargeRow>(salePage, PageSize, saleCount,
                saleRows.Select(item => new ChargeRow(item.SaleId, item.BranchId,
                    item.CashierShiftId, item.CreatedAtUtc, item.Amount,
                    item.Amount - applied.GetValueOrDefault(item.SaleId))).ToArray()),
            new AccountPage<ReceiptRow>(receiptPage, PageSize, receiptCount,
                receiptRows.Select(item =>
                {
                    corrections.TryGetValue(item.Id, out var correction);
                    return new ReceiptRow(item.Id, item.ReceiptNumber, item.BranchId,
                        item.CashierShiftId, item.CreatedAtUtc, item.Amount, item.CreditAmount,
                        MethodName(item.Method), item.Origin == CollectionReceiptOrigin.CashReceived
                            ? "cashReceived" : "reallocation", item.IsVoided, item.ReplacesReceiptId,
                        correction is null ? null : new CorrectionRow(correction.CorrectionNumber,
                            correction.Kind == CustomerCollectionCorrectionKind.Refund ? "refund" : "reallocate",
                            correction.Reason, correction.ReplacementReceiptId, correction.CreatedAtUtc),
                        item.Allocations.Select(allocation => new AllocationRow(allocation.SaleId,
                            allocation.Amount)).ToArray());
                }).ToArray()),
            new AccountPage<CreditApplicationRow>(applicationPage, PageSize, applicationCount,
                applicationRows)));
    }

    private static async Task<Dictionary<Guid, AccountBalance>> GetBalancesAsync(
        PlatformAccessDbContext db, Guid companyId, Guid[] customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Length == 0) return [];
        var charges = await db.CustomerSaleCharges.AsNoTracking()
            .Where(item => item.CompanyId == companyId && customerIds.Contains(item.CustomerId))
            .GroupBy(item => item.CustomerId)
            .Select(group => new { CustomerId = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.CustomerId, item => item.Amount, cancellationToken);
        var allocations = await db.CustomerCollectionAllocations.AsNoTracking()
            .Where(item => item.CompanyId == companyId && customerIds.Contains(item.CustomerId) &&
                !item.Receipt.IsVoided)
            .GroupBy(item => item.CustomerId)
            .Select(group => new { CustomerId = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.CustomerId, item => item.Amount, cancellationToken);
        var receipts = await db.CustomerCollectionReceipts.AsNoTracking()
            .Where(item => item.CompanyId == companyId && customerIds.Contains(item.CustomerId) &&
                !item.IsVoided)
            .GroupBy(item => item.CustomerId)
            .Select(group => new { CustomerId = group.Key, Amount = group.Sum(item => item.CreditAmount) })
            .ToDictionaryAsync(item => item.CustomerId, item => item.Amount, cancellationToken);
        var applications = await db.CustomerCreditApplications.AsNoTracking()
            .Where(item => item.CompanyId == companyId && customerIds.Contains(item.CustomerId))
            .GroupBy(item => item.CustomerId)
            .Select(group => new { CustomerId = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.CustomerId, item => item.Amount, cancellationToken);
        return customerIds.ToDictionary(id => id, id => new AccountBalance(
            charges.GetValueOrDefault(id) - allocations.GetValueOrDefault(id),
            receipts.GetValueOrDefault(id) - applications.GetValueOrDefault(id)));
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string MethodName(Carnicerias.Domain.Sales.PaymentMethod method) => method switch
    {
        Carnicerias.Domain.Sales.PaymentMethod.Cash => "cash",
        Carnicerias.Domain.Sales.PaymentMethod.Debit => "debit",
        Carnicerias.Domain.Sales.PaymentMethod.Credit => "credit",
        Carnicerias.Domain.Sales.PaymentMethod.Transfer => "transfer",
        Carnicerias.Domain.Sales.PaymentMethod.MercadoPago => "mercadoPago",
        Carnicerias.Domain.Sales.PaymentMethod.Cheque => "cheque",
        _ => "unknown"
    };

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])), statusCode: statusCode);

    private readonly record struct AccountBalance(decimal Debt, decimal Credit);
    private sealed record AccountListResponse(int Page, int PageSize, int Total,
        IReadOnlyList<CustomerRow> Items);
    private sealed record CustomerRow(Guid Id, string Code, string Name, bool IsActive,
        bool CreditEnabled, decimal TotalDebt, decimal CreditAvailable);
    private sealed record AccountDetailResponse(Guid CustomerId, string Code, string Name,
        bool IsActive, bool CreditEnabled, decimal TotalDebt, decimal CreditAvailable,
        AccountPage<ChargeRow> Sales, AccountPage<ReceiptRow> Receipts,
        AccountPage<CreditApplicationRow> CreditApplications);
    private sealed record AccountPage<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);
    private sealed record ChargeRow(Guid SaleId, Guid BranchId, Guid CashierShiftId,
        DateTimeOffset ChargedAtUtc, decimal OriginalAmount, decimal OutstandingAmount);
    private sealed record ReceiptRow(Guid Id, long ReceiptNumber, Guid BranchId, Guid CashierShiftId,
        DateTimeOffset CreatedAtUtc, decimal Amount, decimal CreditAmount, string Method,
        string Origin, bool IsVoided, Guid? ReplacesReceiptId, CorrectionRow? Correction,
        IReadOnlyList<AllocationRow> Allocations);
    private sealed record AllocationRow(Guid SaleId, decimal Amount);
    private sealed record CorrectionRow(long CorrectionNumber, string Kind, string Reason,
        Guid? ReplacementReceiptId, DateTimeOffset CreatedAtUtc);
    private sealed record CreditApplicationRow(Guid Id, Guid SaleId, Guid BranchId,
        Guid CashierShiftId, decimal Amount, DateTimeOffset CreatedAtUtc);
}
