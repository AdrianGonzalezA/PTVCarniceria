using Carnicerias.Infrastructure;
using Carnicerias.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Production;

public static partial class ProductionOrderEndpoints
{
    private static async Task<IResult> SummaryAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var orders = db.ProductionOrders.AsNoTracking().Where(order =>
            order.CompanyId == context.CompanyId && order.BranchId == context.BranchId &&
            order.Status == ProductionOrderStatus.Closed);
        var counts = await orders.GroupBy(order => new { order.Kind, order.ClassifierId })
            .Select(group => new { group.Key.Kind, group.Key.ClassifierId,
                OrderCount = group.LongCount(),
                WasteKg = group.Sum(order => order.ReportedWasteKg) })
            .ToArrayAsync(cancellationToken);
        var masses = await db.ProductionMaterialLines.AsNoTracking()
            .Join(orders, line => line.OrderId, order => order.Id,
                (line, order) => new { line, order.Kind, order.ClassifierId })
            .GroupBy(item => new { item.Kind, item.ClassifierId, item.line.Direction })
            .Select(group => new { group.Key.Kind, group.Key.ClassifierId,
                group.Key.Direction, MassKg = group.Sum(item => item.line.MassKg) })
            .ToArrayAsync(cancellationToken);
        var rows = counts.Select(group =>
        {
            var input = masses.Where(item => item.Kind == group.Kind &&
                item.ClassifierId == group.ClassifierId &&
                item.Direction == ProductionLineDirection.Input)
                .Sum(item => item.MassKg);
            var output = masses.Where(item => item.Kind == group.Kind &&
                item.ClassifierId == group.ClassifierId &&
                item.Direction == ProductionLineDirection.Output)
                .Sum(item => item.MassKg);
            return new ProductionSummaryRow(group.Kind.ToString(), group.ClassifierId,
                group.OrderCount, input, output, group.WasteKg,
                input - output - group.WasteKg);
        }).OrderBy(item => item.Kind).ThenBy(item => item.ClassifierId).ToArray();
        return Results.Ok(rows);
    }

    private sealed record ProductionSummaryRow(string Kind, Guid? ClassifierId,
        long OrderCount, decimal InputMassKg, decimal OutputMassKg,
        decimal ReportedWasteKg, decimal MassDifferenceKg);
}
