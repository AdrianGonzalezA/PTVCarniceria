using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carnicerias.Api.Inventory;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/inventory/stock", GetStockAsync).RequireOperationalContext();
        endpoints.MapPost("/api/inventory/adjustments", AdjustStockAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.InventoryStockManage);
        return endpoints;
    }

    private static async Task<IResult> GetStockAsync(
        Guid? productId,
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var query = from product in db.CatalogProducts.AsNoTracking()
                    join stock in db.BranchInventoryBalances.AsNoTracking()
                        .Where(item => item.CompanyId == context.CompanyId && item.BranchId == context.BranchId)
                        on new { product.CompanyId, ProductId = product.Id } equals
                           new { stock.CompanyId, stock.ProductId } into stocks
                    from stock in stocks.DefaultIfEmpty()
                    where product.CompanyId == context.CompanyId && product.IsActive &&
                          (!productId.HasValue || product.Id == productId)
                    orderby product.Name
                    select new StockItem(
                        product.Id,
                        product.Code,
                        product.Name,
                        product.Unit,
                        product.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
                        stock == null ? 0 : stock.OnHand,
                        stock == null ? 0 : stock.Reserved,
                        stock == null ? 0 : stock.OnHand - stock.Reserved);
        return Results.Ok(await query.ToArrayAsync(cancellationToken));
    }

    private static async Task<IResult> AdjustStockAsync(
        AdjustStockRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId == Guid.Empty || request.OperationId == Guid.Empty ||
            request.QuantityDelta == 0 || request.QuantityDelta is > 100000 or < -100000 ||
            decimal.Round(request.QuantityDelta, 3) != request.QuantityDelta ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 240)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = accessor.Context;
        var product = await db.CatalogProducts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.Id == request.ProductId && item.IsActive, cancellationToken);
        if (product is null) return Error(StatusCodes.Status404NotFound, "PRODUCT_NOT_AVAILABLE");
        if (product.SaleMode == ProductSaleMode.Unit && request.QuantityDelta != decimal.Truncate(request.QuantityDelta))
            return Error(StatusCodes.Status400BadRequest, "INVALID_PRODUCT_QUANTITY");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existingOperation = await db.InventoryMovements.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
            item.OperationId == request.OperationId, cancellationToken);
        if (existingOperation is not null)
        {
            if (existingOperation.ProductId != request.ProductId ||
                existingOperation.QuantityDelta != request.QuantityDelta ||
                existingOperation.Reason != request.Reason.Trim())
                return Error(StatusCodes.Status409Conflict, "OPERATION_ID_REUSED");

            var existingBalance = await GetBalanceAsync(db, context.CompanyId, context.BranchId,
                request.ProductId, cancellationToken);
            return Results.Ok(new StockAdjustmentResponse(
                existingOperation.OperationId, existingBalance.OnHand, existingBalance.Reserved,
                existingBalance.OnHand - existingBalance.Reserved));
        }

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO inventory.branch_inventory ("CompanyId", "BranchId", "ProductId", "OnHand", "Reserved")
            VALUES (@companyId, @branchId, @productId, @delta, 0)
            ON CONFLICT ("CompanyId", "BranchId", "ProductId") DO UPDATE
                SET "OnHand" = inventory.branch_inventory."OnHand" + EXCLUDED."OnHand"
                WHERE inventory.branch_inventory."OnHand" + EXCLUDED."OnHand" >=
                      inventory.branch_inventory."Reserved"
            RETURNING 1;
            """;
        AddParameter(command, "companyId", context.CompanyId);
        AddParameter(command, "branchId", context.BranchId);
        AddParameter(command, "productId", request.ProductId);
        AddParameter(command, "delta", request.QuantityDelta);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return Error(StatusCodes.Status409Conflict, "STOCK_BELOW_RESERVED");

        db.InventoryMovements.Add(new InventoryMovement(context.CompanyId, context.BranchId,
            request.ProductId, context.UserId, request.OperationId, InventoryMovementKind.Adjustment,
            request.QuantityDelta, request.Reason, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var balanceAfter = await GetBalanceAsync(db, context.CompanyId, context.BranchId,
            request.ProductId, cancellationToken);
        return Results.Ok(new StockAdjustmentResponse(
            request.OperationId, balanceAfter.OnHand, balanceAfter.Reserved,
            balanceAfter.OnHand - balanceAfter.Reserved));
    }

    private static Task<BranchInventoryBalance> GetBalanceAsync(
        PlatformAccessDbContext db, Guid companyId, Guid branchId, Guid productId,
        CancellationToken cancellationToken) => db.BranchInventoryBalances.AsNoTracking().SingleAsync(balance =>
            balance.CompanyId == companyId && balance.BranchId == branchId && balance.ProductId == productId,
            cancellationToken);

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new Carnicerias.Api.Contracts.ErrorResponse(
            new Carnicerias.Api.Contracts.ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record AdjustStockRequest(Guid ProductId, Guid OperationId, decimal QuantityDelta, string Reason);
    private sealed record StockItem(Guid ProductId, string Code, string Name, string Unit,
        string SaleMode, decimal OnHand, decimal Reserved, decimal Available);
    private sealed record StockAdjustmentResponse(Guid OperationId, decimal OnHand, decimal Reserved, decimal Available);
}
