using Carnicerias.Domain.PlatformAccess;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Inventory;

public sealed class CashierShiftContextChangeBlocker(PlatformAccessDbContext db)
    : IOperationalContextChangeBlocker
{
    public async Task<IReadOnlyList<OperationalContextChangeBlock>> GetBlocksAsync(
        OperationalContext context,
        CancellationToken cancellationToken = default)
    {
        var hasOpenShift = await db.CashierShifts.AsNoTracking().AnyAsync(shift =>
            shift.CompanyId == context.CompanyId && shift.BranchId == context.BranchId &&
            shift.CashierId == context.UserId && shift.Status == CashierShiftStatus.Open,
            cancellationToken);

        return hasOpenShift
            ? [new OperationalContextChangeBlock(
                "CASHIER_SHIFT_OPEN", "Cerrá tu turno de caja antes de cambiar de sucursal.")]
            : [];
    }
}
