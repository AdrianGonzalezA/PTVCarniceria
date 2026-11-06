using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Production;

public static partial class ProductionOrderEndpoints
{
    private static async Task<IResult> ListOptionsAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var products = await db.CatalogProducts.AsNoTracking().Where(item =>
                item.CompanyId == context.CompanyId && item.IsActive)
            .OrderBy(item => item.Name).ThenBy(item => item.Code)
            .Select(item => new ProductOption(item.Id, item.Code, item.Name,
                item.Unit, item.SaleMode.ToString()))
            .ToArrayAsync(cancellationToken);
        var profiles = await db.BarcodeProfiles.AsNoTracking().Where(item =>
                item.CompanyId == context.CompanyId)
            .OrderBy(item => item.Name).ThenByDescending(item => item.Revision)
            .Select(item => new ProfileOption(item.Id, item.Name, item.Revision,
                item.Formula, item.WeightField, item.WeightDecimals))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new { products, profiles });
    }

    private static async Task<IResult> ListPiecesAsync(Guid productId, Guid? orderId,
        string? query, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty || query?.Length > 80 || orderId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (orderId is Guid requestedOrderId && !await db.ProductionOrders.AsNoTracking()
                .AnyAsync(item => item.CompanyId == context.CompanyId &&
                    item.BranchId == context.BranchId && item.Id == requestedOrderId &&
                    item.Status == ProductionOrderStatus.Open, cancellationToken))
            return Error(404, "PRODUCTION_ORDER_NOT_FOUND");
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var pieces = await db.InventoryPieces.AsNoTracking().Where(item =>
                item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.ProductId == productId &&
                (item.Status == InventoryPieceStatus.Available ||
                 orderId != null && item.Status == InventoryPieceStatus.Reserved &&
                 item.ReservedByProductionOrderId == orderId) &&
                (normalizedQuery == string.Empty ||
                 item.ExternalIdentifier.Contains(normalizedQuery) ||
                 item.RawBarcode.Contains(normalizedQuery)))
            .OrderBy(item => item.ExternalIdentifier).ThenBy(item => item.Id)
            .Take(50)
            .Select(item => new PieceOption(item.Id, item.ExternalIdentifier,
                item.ReceivedWeightKg, item.RawBarcode,
                orderId != null && item.ReservedByProductionOrderId == orderId))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(pieces);
    }

    private sealed record ProductOption(Guid Id, string Code, string Name,
        string Unit, string SaleMode);
    private sealed record ProfileOption(Guid Id, string Name, int Revision,
        string Formula, string WeightField, int WeightDecimals);
    private sealed record PieceOption(Guid Id, string ExternalIdentifier,
        decimal WeightKg, string RawBarcode, bool OwnedByOrder);
}
