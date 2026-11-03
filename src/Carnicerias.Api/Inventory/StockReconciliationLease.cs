using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Inventory;

internal static class StockReconciliationLease
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMinutes(3);

    internal static IQueryable<BranchInventoryBalance> Unlocked(
        IQueryable<BranchInventoryBalance> balances, DateTimeOffset now) =>
        balances.Where(balance => balance.ReconciliationLockExpiresAtUtc == null ||
            balance.ReconciliationLockExpiresAtUtc <= now);

    internal static bool IsActive(BranchInventoryBalance balance, DateTimeOffset now) =>
        balance.IsReconciliationLocked(now);

    internal static async Task<bool> IsLockedForUpdateAsync(PlatformAccessDbContext db,
        Guid companyId, Guid branchId, Guid productId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await db.BranchInventoryBalances.FromSqlInterpolated($"""
            SELECT * FROM inventory.branch_inventory
            WHERE "CompanyId" = {companyId} AND "BranchId" = {branchId}
                AND "ProductId" = {productId}
            FOR UPDATE
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        return rows.Length == 1 && IsActive(rows[0], now);
    }
}
