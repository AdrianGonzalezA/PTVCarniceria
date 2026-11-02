using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Sales;

/// <summary>Serializes draft edits, payment creation and exceptional release.</summary>
internal static class SaleDraftTransactionLock
{
    public static async Task<bool> AcquireAsync(PlatformAccessDbContext db, Guid companyId,
        Guid branchId, Guid draftId, CancellationToken cancellationToken)
    {
        var locked = await db.SaleDrafts.FromSqlInterpolated($"""
            SELECT * FROM pos_sales.sale_drafts
            WHERE "Id" = {draftId} AND "CompanyId" = {companyId} AND "BranchId" = {branchId}
            FOR UPDATE
            """).ToArrayAsync(cancellationToken);
        return locked.Length == 1;
    }
}
