using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Carnicerias.Api.Sales;

public static class SaleDraftEndpoints
{
    public static IEndpointRouteBuilder MapSaleDraftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/sales/draft", (PlatformAccessDbContext db,
            OperationalContextAccessor accessor, CancellationToken cancellationToken) =>
            GetAsync("A", db, accessor, cancellationToken)).RequireOperationalContext();
        endpoints.MapPut("/api/sales/draft", (SaveSaleDraftRequest? request, PlatformAccessDbContext db,
            OperationalContextAccessor accessor, TimeProvider timeProvider, CancellationToken cancellationToken) =>
            SaveAsync("A", request, db, accessor, timeProvider, cancellationToken)).RequireOperationalContext();
        endpoints.MapDelete("/api/sales/draft", (PlatformAccessDbContext db,
            OperationalContextAccessor accessor, TimeProvider timeProvider, CancellationToken cancellationToken) =>
            CancelAsync("A", db, accessor, timeProvider, cancellationToken)).RequireOperationalContext();
        endpoints.MapGet("/api/sales/drafts", ListAsync).RequireOperationalContext();
        endpoints.MapGet("/api/sales/drafts/{slot}", GetAsync).RequireOperationalContext();
        endpoints.MapPut("/api/sales/drafts/{slot}", SaveAsync).RequireOperationalContext();
        endpoints.MapDelete("/api/sales/drafts/{slot}", CancelAsync).RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var shift = await FindOpenShiftAsync(db, accessor, cancellationToken);
        if (shift is null) return Results.Ok(Array.Empty<SaleDraftResponse>());
        var drafts = await db.SaleDrafts.AsNoTracking().Include(item => item.Lines)
            .Where(item => item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.UserId == context.UserId &&
                item.PosTerminalId == accessor.TerminalId &&
                (accessor.TerminalId == null || item.CashierShiftId == shift.Id) &&
                item.Status == SaleDraftStatus.Draft)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(drafts.OrderBy(item => item.TicketSlot).Select(ToResponse).ToArray());
    }

    private static async Task<IResult> GetAsync(
        string slot,
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        if (!TryTicketSlot(slot, out var ticketSlot))
            return Error(StatusCodes.Status400BadRequest, "INVALID_TICKET_SLOT");
        var context = accessor.Context;
        var shift = await FindOpenShiftAsync(db, accessor, cancellationToken);
        if (shift is null) return Results.NoContent();
        var draft = await db.SaleDrafts.AsNoTracking().Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.UserId == context.UserId &&
                item.PosTerminalId == accessor.TerminalId &&
                (accessor.TerminalId == null || item.CashierShiftId == shift.Id) &&
                item.TicketSlot == ticketSlot &&
                item.Status == SaleDraftStatus.Draft, cancellationToken);
        return draft is null ? Results.NoContent() : Results.Ok(ToResponse(draft));
    }

    private static async Task<IResult> SaveAsync(
        string slot,
        SaveSaleDraftRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryTicketSlot(slot, out var ticketSlot))
            return Error(StatusCodes.Status400BadRequest, "INVALID_TICKET_SLOT");
        if (request is null || request.PriceListId == Guid.Empty || request.Lines is null ||
            request.Lines.Count is < 1 or > 100 || request.Lines.Any(line =>
                line.ProductId == Guid.Empty || line.Quantity <= 0 || line.Quantity > 10000 ||
                decimal.Round(line.Quantity, 3) != line.Quantity || line.InventoryPieceId == Guid.Empty) ||
            request.Lines.Select(line => (line.ProductId, line.InventoryPieceId)).Distinct().Count() != request.Lines.Count ||
            request.Lines.Where(line => line.InventoryPieceId is not null)
                .Select(line => line.InventoryPieceId).Distinct().Count() !=
            request.Lines.Count(line => line.InventoryPieceId is not null) ||
            request.DiscountAmount < 0 || decimal.Round(request.DiscountAmount, 2) != request.DiscountAmount ||
            (request.DiscountAmount > 0 && (string.IsNullOrWhiteSpace(request.DiscountReason) ||
                request.DiscountReason.Trim().Length is < 10 or > 200)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = accessor.Context;
        if (request.DiscountAmount > 0 &&
            !context.Permissions.Contains(PlatformPermissionCatalog.PosDiscountApply))
            return Error(StatusCodes.Status403Forbidden, "DISCOUNT_FORBIDDEN");
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var shift = await FindOpenShiftAsync(db, accessor, cancellationToken);
        if (shift is null) return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_REQUIRED");
        var listAvailable = await (
            from assignment in db.BranchPriceLists
            join list in db.PriceLists on assignment.PriceListId equals list.Id
            where assignment.CompanyId == context.CompanyId && assignment.BranchId == context.BranchId &&
                  assignment.PriceListId == request.PriceListId && assignment.IsActive && list.IsActive
            select assignment.PriceListId).AnyAsync(cancellationToken);
        if (!listAvailable) return Error(StatusCodes.Status403Forbidden, "PRICE_LIST_NOT_AVAILABLE");

        var draft = await db.SaleDrafts.Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.UserId == context.UserId &&
                item.PosTerminalId == accessor.TerminalId &&
                (accessor.TerminalId == null || item.CashierShiftId == shift.Id) &&
                item.TicketSlot == ticketSlot &&
                item.Status == SaleDraftStatus.Draft, cancellationToken);
        if (draft is null && await db.SaleDrafts.AnyAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.UserId == context.UserId && item.TicketSlot == ticketSlot &&
                item.Status == SaleDraftStatus.Draft, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "DRAFT_BELONGS_TO_OTHER_SHIFT");
        if (draft is not null && draft.PriceListId != request.PriceListId)
            return Error(StatusCodes.Status409Conflict, "DRAFT_PRICE_LIST_LOCKED");

        var currentDraftId = draft?.Id ?? Guid.Empty;
        var lines = new List<SaleDraftLine>(request.Lines.Count);
        foreach (var requestedLine in request.Lines)
        {
            var product = await db.CatalogProducts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.Id == requestedLine.ProductId && item.IsActive,
                cancellationToken);
            if (product is null) return Error(StatusCodes.Status400BadRequest, "PRODUCT_NOT_AVAILABLE");
            var existingLine = draft?.Lines.SingleOrDefault(line => line.ProductId == product.Id &&
                line.InventoryPieceId == requestedLine.InventoryPieceId);
            var saleMode = existingLine?.SaleMode ?? product.SaleMode;
            if ((saleMode == ProductSaleMode.Unit && requestedLine.Quantity != decimal.Truncate(requestedLine.Quantity)) ||
                (saleMode == ProductSaleMode.Weight && requestedLine.Quantity <= 0))
                return Error(StatusCodes.Status400BadRequest, "INVALID_PRODUCT_QUANTITY");

            InventoryPiece? piece = null;
            if (requestedLine.InventoryPieceId is Guid pieceId)
            {
                piece = await db.InventoryPieces.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.Id == pieceId && item.CompanyId == context.CompanyId &&
                    item.BranchId == context.BranchId && item.ProductId == product.Id,
                    cancellationToken);
                if (piece is null || saleMode != ProductSaleMode.Weight ||
                    piece.ReceivedWeightKg != requestedLine.Quantity)
                    return Error(StatusCodes.Status409Conflict, "PIECE_NOT_AVAILABLE");
                if (await db.ConfirmedSaleLines.AnyAsync(line => line.InventoryPieceId == pieceId, cancellationToken) ||
                    await db.Set<SaleDraftLine>().AnyAsync(line => line.InventoryPieceId == pieceId &&
                        line.SaleDraftId != currentDraftId && db.SaleDrafts.Any(other =>
                            other.Id == line.SaleDraftId && other.Status == SaleDraftStatus.Draft), cancellationToken))
                    return Error(StatusCodes.Status409Conflict, "PIECE_ALREADY_USED");
            }

            var price = existingLine?.UnitPrice ?? await db.ProductPrices.AsNoTracking()
                .Where(item => item.CompanyId == context.CompanyId && item.PriceListId == request.PriceListId &&
                    item.ProductId == product.Id && item.EffectiveFromUtc <= now &&
                    (item.EffectiveToUtc == null || item.EffectiveToUtc > now))
                .OrderByDescending(item => item.EffectiveFromUtc)
                .Select(item => (decimal?)item.Amount)
                .FirstOrDefaultAsync(cancellationToken) ?? 0;
            if (price <= 0) return Error(StatusCodes.Status400BadRequest, "PRODUCT_PRICE_NOT_AVAILABLE");
            lines.Add(new SaleDraftLine(context.CompanyId, product.Id,
                existingLine?.ProductCode ?? product.Code,
                existingLine?.ProductName ?? product.Name,
                existingLine?.Unit ?? product.Unit, saleMode, requestedLine.Quantity, price,
                piece?.Id, piece?.ExternalIdentifier));
        }

        var undiscountedTotal = lines.Sum(line => decimal.Round(line.Quantity * line.UnitPrice,
            2, MidpointRounding.AwayFromZero));
        if (request.DiscountAmount >= undiscountedTotal)
            return Error(StatusCodes.Status400BadRequest, "DISCOUNT_EXCEEDS_TOTAL");

        if (draft is null)
        {
            draft = new SaleDraft(context.CompanyId, context.BranchId, context.UserId, request.PriceListId, now,
                accessor.TerminalId, accessor.TerminalId is null ? null : shift.Id, ticketSlot);
            db.SaleDrafts.Add(draft);
        }
        foreach (var productId in lines.Select(line => line.ProductId)
                     .Concat(draft.Lines.Select(line => line.ProductId)).Distinct())
        {
            var previousQuantity = draft.Lines.Where(line => line.ProductId == productId).Sum(line => line.Quantity);
            var newQuantity = lines.Where(line => line.ProductId == productId).Sum(line => line.Quantity);
            var quantityDelta = newQuantity - previousQuantity;
            if (quantityDelta == 0) continue;

            var stock = db.BranchInventoryBalances.Where(balance => balance.CompanyId == context.CompanyId &&
                balance.BranchId == context.BranchId && balance.ProductId == productId);
            var updated = quantityDelta > 0
                ? await stock.Where(balance => balance.OnHand - balance.Reserved >= quantityDelta)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        balance => balance.Reserved, balance => balance.Reserved + quantityDelta), cancellationToken)
                : await stock.Where(balance => balance.Reserved >= -quantityDelta)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        balance => balance.Reserved, balance => balance.Reserved + quantityDelta), cancellationToken);
            if (updated == 0)
                return Error(StatusCodes.Status409Conflict,
                    quantityDelta > 0 ? "INSUFFICIENT_STOCK" : "STOCK_RESERVATION_MISSING");
        }

        draft.ReplaceLines(lines, now);
        draft.SetDiscount(request.DiscountAmount, request.DiscountReason);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(ToResponse(draft));
    }

    private static async Task<IResult> CancelAsync(
        string slot,
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryTicketSlot(slot, out var ticketSlot))
            return Error(StatusCodes.Status400BadRequest, "INVALID_TICKET_SLOT");
        var context = accessor.Context;
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var shift = await FindOpenShiftAsync(db, accessor, cancellationToken);
        if (shift is null) return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_REQUIRED");
        var draft = await db.SaleDrafts.Include(item => item.Lines).SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
            item.BranchId == context.BranchId && item.UserId == context.UserId &&
            item.PosTerminalId == accessor.TerminalId &&
            (accessor.TerminalId == null || item.CashierShiftId == shift.Id) &&
            item.TicketSlot == ticketSlot &&
            item.Status == SaleDraftStatus.Draft, cancellationToken);
        if (draft is null) return Results.NoContent();
        foreach (var group in draft.Lines.GroupBy(line => line.ProductId))
        {
            var quantity = group.Sum(line => line.Quantity);
            var updated = await db.BranchInventoryBalances.Where(balance =>
                    balance.CompanyId == context.CompanyId && balance.BranchId == context.BranchId &&
                    balance.ProductId == group.Key && balance.Reserved >= quantity)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    balance => balance.Reserved, balance => balance.Reserved - quantity), cancellationToken);
            if (updated == 0)
                return Error(StatusCodes.Status409Conflict, "STOCK_RESERVATION_MISSING");
        }
        draft.Cancel(context.UserId, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Task<CashierShift?> FindOpenShiftAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        return db.CashierShifts.SingleOrDefaultAsync(shift =>
            shift.CompanyId == context.CompanyId && shift.BranchId == context.BranchId &&
            shift.CashierId == context.UserId && shift.PosTerminalId == accessor.TerminalId &&
            shift.Status == CashierShiftStatus.Open, cancellationToken);
    }

    private static bool TryTicketSlot(string raw, out SaleTicketSlot slot)
    {
        slot = SaleTicketSlot.A;
        if (raw.Length != 1 || raw[0] is < 'A' or > 'D') return false;
        slot = (SaleTicketSlot)(raw[0] - 'A');
        return true;
    }

    private static SaleDraftResponse ToResponse(SaleDraft draft) => new(
        draft.Id, draft.TicketSlot.ToString(), draft.PriceListId, draft.UpdatedAtUtc,
        draft.DiscountAmount, draft.DiscountReason,
        draft.Lines.OrderBy(line => line.ProductName).ThenBy(line => line.PieceIdentifier)
            .Select(line => new SaleDraftLineResponse(
                line.Id, line.ProductId, line.ProductCode, line.ProductName, line.Unit,
                line.SaleMode == ProductSaleMode.Weight ? "weight" : "unit", line.Quantity, line.UnitPrice,
                line.InventoryPieceId, line.PieceIdentifier)).ToArray());

    private static IResult Error(int statusCode, string code) => Results.Json(
        new Carnicerias.Api.Contracts.ErrorResponse(
            new Carnicerias.Api.Contracts.ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record SaveSaleDraftRequest(Guid PriceListId, IReadOnlyList<SaveSaleDraftLineRequest> Lines,
        decimal DiscountAmount = 0, string? DiscountReason = null);
    private sealed record SaveSaleDraftLineRequest(Guid ProductId, decimal Quantity, Guid? InventoryPieceId = null);
    private sealed record SaleDraftResponse(Guid Id, string TicketSlot, Guid PriceListId, DateTimeOffset UpdatedAtUtc,
        decimal DiscountAmount, string? DiscountReason,
        IReadOnlyList<SaleDraftLineResponse> Lines);
    private sealed record SaleDraftLineResponse(Guid Id, Guid ProductId, string ProductCode, string ProductName,
        string Unit, string SaleMode, decimal Quantity, decimal UnitPrice,
        Guid? InventoryPieceId, string? PieceIdentifier);
}
