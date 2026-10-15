using System.Data;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Federation;

public static class BranchSummaryQuery
{
    public static IQueryable<ConfirmedSale> ScopeSales(IQueryable<ConfirmedSale> sales,
        Guid companyId, Guid branchId, BranchSummaryWindow window) => sales.Where(sale =>
        sale.CompanyId == companyId && sale.BranchId == branchId &&
        sale.ConfirmedAtUtc >= window.FromUtc && sale.ConfirmedAtUtc < window.ToUtc);

    public static IQueryable<CashierShift> ScopeOpenShifts(IQueryable<CashierShift> shifts,
        Guid companyId, Guid branchId) => shifts.Where(shift =>
        shift.CompanyId == companyId && shift.BranchId == branchId &&
        shift.Status == CashierShiftStatus.Open);

    public static async Task<BranchSummaryData> ReadAsync(PlatformAccessDbContext db,
        Guid companyId, Guid branchId, BranchSummaryWindow window,
        CancellationToken cancellationToken)
    {
        // A sale and its payments must be observed in the same database snapshot.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var sales = ScopeSales(db.ConfirmedSales.AsNoTracking(), companyId, branchId, window);
        var totals = await sales.GroupBy(_ => 1).Select(group => new
        {
            Count = group.Count(),
            Total = group.Sum(sale => sale.Total)
        }).SingleOrDefaultAsync(cancellationToken);
        var paymentRows = await sales.SelectMany(sale => sale.Payments)
            .GroupBy(payment => payment.Method)
            .Select(group => new { Method = group.Key, Amount = group.Sum(payment => payment.AppliedAmount) })
            .ToArrayAsync(cancellationToken);
        var payments = paymentRows.OrderBy(row => row.Method)
            .Select(row => new BranchPaymentSummary(PaymentMethodName(row.Method), row.Amount))
            .ToArray();

        var openShifts = ScopeOpenShifts(db.CashierShifts.AsNoTracking(), companyId, branchId);
        var openShiftCount = await openShifts.CountAsync(cancellationToken);
        var openShiftIds = openShifts.Select(shift => shift.Id);
        var cashBalance = await db.CashLedgerMovements.AsNoTracking()
            .Where(movement => movement.CompanyId == companyId && movement.BranchId == branchId &&
                movement.Method == PaymentMethod.Cash && openShiftIds.Contains(movement.CashierShiftId))
            .SumAsync(movement => (decimal?)movement.AmountDelta, cancellationToken) ?? 0;

        var summary = new BranchSummaryData(totals?.Count ?? 0, totals?.Total ?? 0,
            payments.Sum(payment => payment.Amount), payments, openShiftCount, cashBalance);
        await transaction.CommitAsync(cancellationToken);
        return summary;
    }

    private static string PaymentMethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "cash",
        PaymentMethod.Debit => "debit",
        PaymentMethod.Credit => "credit",
        PaymentMethod.Transfer => "transfer",
        PaymentMethod.MercadoPago => "mercadoPago",
        PaymentMethod.Cheque => "cheque",
        _ => "unknown"
    };
}

public sealed record BranchSummaryData(int SaleCount, decimal SalesTotal,
    decimal SalePaymentsTotal, BranchPaymentSummary[] PaymentsByMethod,
    int OpenShiftCount, decimal OpenShiftCashBalance);

public sealed record BranchPaymentSummary(string Method, decimal Amount);
