using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.History;

public static class AdminHistoryEndpoints
{
    public static IEndpointRouteBuilder MapAdminHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var history = endpoints.MapGroup("/api/admin/history")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        history.MapGet("/sales", ListSalesAsync);
        history.MapGet("/summary", GetSummaryAsync);
        history.MapGet("/sales/{saleId:guid}", GetSaleAsync);
        history.MapGet("/shifts", ListShiftsAsync);
        history.MapGet("/cash-movements", ListCashMovementsAsync);
        history.MapGet("/stock-movements", ListStockMovementsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetSummaryAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor, TimeProvider timeProvider,
        CancellationToken cancellationToken, DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null, Guid? branchId = null)
    {
        if (branchId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        BusinessSummaryWindow window;
        if (fromUtc.HasValue || toUtc.HasValue)
        {
            if (!BusinessSummaryWindow.TryCreate(fromUtc, toUtc, out window))
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }
        else window = BusinessSummaryWindow.TodayArgentina(timeProvider);
        var companyId = accessor.Context.CompanyId;
        if (branchId.HasValue && !await db.Branches.AsNoTracking().AnyAsync(branch =>
                branch.Id == branchId && branch.CompanyId == companyId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");

        var sales = db.ConfirmedSales.AsNoTracking().Where(sale =>
            sale.CompanyId == companyId && (!branchId.HasValue || sale.BranchId == branchId) &&
            sale.ConfirmedAtUtc >= window.FromUtc && sale.ConfirmedAtUtc < window.ToUtc);
        var saleTotals = await sales.GroupBy(_ => 1).Select(group => new
        {
            Count = group.Count(), Total = group.Sum(sale => sale.Total)
        }).SingleOrDefaultAsync(cancellationToken);
        var paymentRows = await sales.SelectMany(sale => sale.Payments)
            .GroupBy(payment => payment.Method)
            .Select(group => new { Method = group.Key, Amount = group.Sum(payment => payment.AppliedAmount) })
            .ToArrayAsync(cancellationToken);
        var payments = paymentRows.OrderBy(row => row.Method)
            .Select(row => new PaymentSummary(PaymentMethodName(row.Method), row.Amount)).ToArray();
        var creditApplied = await sales.SumAsync(sale => (decimal?)sale.CreditAppliedAmount,
            cancellationToken) ?? 0;

        var charges = db.CustomerSaleCharges.AsNoTracking().Where(charge =>
            charge.CompanyId == companyId && (!branchId.HasValue || charge.BranchId == branchId));
        var newCharges = await charges.Where(charge => charge.CreatedAtUtc >= window.FromUtc &&
            charge.CreatedAtUtc < window.ToUtc).SumAsync(charge => (decimal?)charge.Amount,
            cancellationToken) ?? 0;
        var registeredCharges = await charges.SumAsync(charge => (decimal?)charge.Amount,
            cancellationToken) ?? 0;
        var chargeSales = charges.Select(charge => charge.SaleId);
        var allocatedToCharges = await db.CustomerCollectionAllocations.AsNoTracking()
            .Where(item => item.CompanyId == companyId && !item.Receipt.IsVoided &&
                chargeSales.Contains(item.SaleId))
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
        var companyCredit = await db.CustomerCollectionReceipts.AsNoTracking()
            .Where(item => item.CompanyId == companyId && !item.IsVoided)
            .SumAsync(item => (decimal?)item.CreditAmount, cancellationToken) ?? 0;
        companyCredit -= await db.CustomerCreditApplications.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
        var collections = await db.CashLedgerMovements.AsNoTracking()
            .Where(item => item.CompanyId == companyId &&
                (!branchId.HasValue || item.BranchId == branchId) &&
                item.CreatedAtUtc >= window.FromUtc && item.CreatedAtUtc < window.ToUtc &&
                (item.Kind == CashLedgerMovementKind.AccountCollection ||
                 item.Kind == CashLedgerMovementKind.AccountCollectionRefund))
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Received = group.Where(item => item.Kind == CashLedgerMovementKind.AccountCollection)
                    .Sum(item => item.AmountDelta),
                Refunded = group.Where(item => item.Kind == CashLedgerMovementKind.AccountCollectionRefund)
                    .Sum(item => -item.AmountDelta),
                CashNet = group.Where(item => item.Method == PaymentMethod.Cash)
                    .Sum(item => item.AmountDelta),
                NonCashNet = group.Where(item => item.Method != PaymentMethod.Cash)
                    .Sum(item => item.AmountDelta)
            }).SingleOrDefaultAsync(cancellationToken);
        var openShifts = db.CashierShifts.AsNoTracking().Where(shift =>
            shift.CompanyId == companyId && (!branchId.HasValue || shift.BranchId == branchId) &&
            shift.Status == CashierShiftStatus.Open);
        var openShiftCount = await openShifts.CountAsync(cancellationToken);
        var openShiftIds = openShifts.Select(shift => shift.Id);
        var openShiftCash = await db.CashLedgerMovements.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Method == PaymentMethod.Cash &&
                openShiftIds.Contains(item.CashierShiftId))
            .SumAsync(item => (decimal?)item.AmountDelta, cancellationToken) ?? 0;
        return Results.Ok(new BusinessSummary(window.FromUtc, window.ToUtc, branchId,
            saleTotals?.Count ?? 0, saleTotals?.Total ?? 0, payments.Sum(payment => payment.Amount),
            payments, newCharges, registeredCharges, creditApplied,
            registeredCharges - allocatedToCharges, companyCredit,
            collections?.Received ?? 0, collections?.Refunded ?? 0,
            collections?.CashNet ?? 0, collections?.NonCashNet ?? 0,
            openShiftCount, openShiftCash));
    }

    private static async Task<IResult> ListSalesAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 30, Guid? branchId = null, Guid? terminalId = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null)
    {
        var check = await ValidateAsync(db, accessor.Context.CompanyId, page, pageSize,
            branchId, terminalId, fromUtc, toUtc, cancellationToken);
        if (check is not null) return check;
        var companyId = accessor.Context.CompanyId;
        var query = db.ConfirmedSales.AsNoTracking().Where(sale => sale.CompanyId == companyId &&
            (!branchId.HasValue || sale.BranchId == branchId.Value) &&
            (!terminalId.HasValue || sale.PosTerminalId == terminalId.Value) &&
            (!fromUtc.HasValue || sale.ConfirmedAtUtc >= fromUtc.Value) &&
            (!toUtc.HasValue || sale.ConfirmedAtUtc < toUtc.Value));
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(sale => sale.ConfirmedAtUtc).ThenByDescending(sale => sale.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(sale => new SaleItem(sale.Id, sale.ConfirmedAtUtc, sale.Total,
                sale.BranchId, db.Branches.Where(branch => branch.Id == sale.BranchId)
                    .Select(branch => branch.Name).FirstOrDefault() ?? "",
                sale.PosTerminalId, db.PosTerminals.Where(terminal => terminal.Id == sale.PosTerminalId)
                    .Select(terminal => terminal.Name).FirstOrDefault(),
                sale.CashierId, db.Users.Where(user => user.Id == sale.CashierId)
                    .Select(user => user.Username).FirstOrDefault() ?? "",
                sale.CashierShiftId))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new HistoryPage<SaleItem>(items, page, pageSize, total));
    }

    private static async Task<IResult> GetSaleAsync(
        Guid saleId, PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var sale = await db.ConfirmedSales.AsNoTracking().AsSplitQuery()
            .Include(item => item.Lines).Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.Id == saleId &&
                item.CompanyId == accessor.Context.CompanyId, cancellationToken);
        if (sale is null) return Error(StatusCodes.Status404NotFound, "SALE_NOT_FOUND");
        return Results.Ok(new SaleDetail(
            sale.Id,
            sale.ConfirmedAtUtc,
            sale.Total,
            sale.DiscountAmount,
            sale.DiscountReason,
            sale.Lines.OrderBy(line => line.ProductName).Select(line => new SaleLineItem(
                line.ProductCode, line.ProductName, line.Unit,
                line.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
                line.Quantity, line.UnitPrice, line.LineTotal)).ToArray(),
            sale.Payments.OrderBy(payment => payment.Method).Select(payment => new SalePaymentItem(
                PaymentMethodName(payment.Method), payment.TenderedAmount, payment.AppliedAmount)).ToArray()));
    }

    private static async Task<IResult> ListShiftsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 30, Guid? branchId = null, Guid? terminalId = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null)
    {
        var check = await ValidateAsync(db, accessor.Context.CompanyId, page, pageSize,
            branchId, terminalId, fromUtc, toUtc, cancellationToken);
        if (check is not null) return check;
        var companyId = accessor.Context.CompanyId;
        var query = db.CashierShifts.AsNoTracking().Where(shift => shift.CompanyId == companyId &&
            (!branchId.HasValue || shift.BranchId == branchId.Value) &&
            (!terminalId.HasValue || shift.PosTerminalId == terminalId.Value) &&
            (!fromUtc.HasValue || shift.OpenedAtUtc >= fromUtc.Value) &&
            (!toUtc.HasValue || shift.OpenedAtUtc < toUtc.Value));
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(shift => shift.OpenedAtUtc).ThenByDescending(shift => shift.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(shift => new ShiftRow(shift.Id, shift.OpenedAtUtc, shift.ClosedAtUtc,
                shift.Status, shift.OpeningCash, shift.BranchId,
                db.Branches.Where(branch => branch.Id == shift.BranchId)
                    .Select(branch => branch.Name).FirstOrDefault() ?? "",
                shift.PosTerminalId, db.PosTerminals.Where(terminal => terminal.Id == shift.PosTerminalId)
                    .Select(terminal => terminal.Name).FirstOrDefault(),
                shift.CashierId, db.Users.Where(user => user.Id == shift.CashierId)
                    .Select(user => user.Username).FirstOrDefault() ?? ""))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new HistoryPage<ShiftItem>(rows.Select(row => new ShiftItem(
            row.Id, row.OpenedAtUtc, row.ClosedAtUtc,
            row.Status == CashierShiftStatus.Open ? "open" : "closed", row.OpeningCash,
            row.BranchId, row.BranchName, row.TerminalId, row.TerminalName,
            row.CashierId, row.CashierName)).ToArray(), page, pageSize, total));
    }

    private static async Task<IResult> ListCashMovementsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 30, Guid? branchId = null, Guid? terminalId = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null)
    {
        var check = await ValidateAsync(db, accessor.Context.CompanyId, page, pageSize,
            branchId, terminalId, fromUtc, toUtc, cancellationToken);
        if (check is not null) return check;
        var companyId = accessor.Context.CompanyId;
        var query = db.CashLedgerMovements.AsNoTracking().Where(item => item.CompanyId == companyId &&
            (!branchId.HasValue || item.BranchId == branchId.Value) &&
            (!terminalId.HasValue || item.PosTerminalId == terminalId.Value) &&
            (!fromUtc.HasValue || item.CreatedAtUtc >= fromUtc.Value) &&
            (!toUtc.HasValue || item.CreatedAtUtc < toUtc.Value));
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new CashRow(item.Id, item.CreatedAtUtc, item.BranchId,
                db.Branches.Where(branch => branch.Id == item.BranchId)
                    .Select(branch => branch.Name).FirstOrDefault() ?? "",
                item.PosTerminalId, db.PosTerminals.Where(terminal => terminal.Id == item.PosTerminalId)
                    .Select(terminal => terminal.Name).FirstOrDefault(),
                item.CashierId, db.Users.Where(user => user.Id == item.CashierId)
                    .Select(user => user.Username).FirstOrDefault() ?? "",
                item.CashierShiftId, item.SaleId, item.Kind, item.Method, item.AmountDelta))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new HistoryPage<CashItem>(rows.Select(row => new CashItem(
            row.Id, row.CreatedAtUtc, row.BranchId, row.BranchName, row.TerminalId,
            row.TerminalName, row.CashierId, row.CashierName, row.ShiftId, row.SaleId,
            row.Kind switch
            {
                CashLedgerMovementKind.Opening => "opening",
                CashLedgerMovementKind.SalePayment => "salePayment",
                CashLedgerMovementKind.Change => "change",
                CashLedgerMovementKind.AccountCollection => "accountCollection",
                CashLedgerMovementKind.AccountCollectionRefund => "accountCollectionRefund",
                _ => "unknown"
            }, PaymentMethodName(row.Method), row.AmountDelta)).ToArray(), page, pageSize, total));
    }

    private static async Task<IResult> ListStockMovementsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 30, Guid? branchId = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null)
    {
        var check = await ValidateAsync(db, accessor.Context.CompanyId, page, pageSize,
            branchId, null, fromUtc, toUtc, cancellationToken);
        if (check is not null) return check;
        var companyId = accessor.Context.CompanyId;
        var query = db.InventoryMovements.AsNoTracking().Where(item => item.CompanyId == companyId &&
            (!branchId.HasValue || item.BranchId == branchId.Value) &&
            (!fromUtc.HasValue || item.CreatedAtUtc >= fromUtc.Value) &&
            (!toUtc.HasValue || item.CreatedAtUtc < toUtc.Value));
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new StockRow(item.Id, item.CreatedAtUtc, item.BranchId,
                db.Branches.Where(branch => branch.Id == item.BranchId)
                    .Select(branch => branch.Name).FirstOrDefault() ?? "",
                item.ProductId, db.CatalogProducts.Where(product => product.Id == item.ProductId)
                    .Select(product => product.Name).FirstOrDefault() ?? "",
                item.UserId, db.Users.Where(user => user.Id == item.UserId)
                    .Select(user => user.Username).FirstOrDefault() ?? "",
                item.Kind, item.QuantityDelta, item.Reason))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new HistoryPage<StockItem>(rows.Select(row => new StockItem(
            row.Id, row.CreatedAtUtc, row.BranchId, row.BranchName, row.ProductId,
            row.ProductName, row.UserId, row.Username, row.Kind switch
            {
                InventoryMovementKind.OpeningBalance => "openingBalance",
                InventoryMovementKind.Adjustment => "adjustment",
                _ => "sale"
            }, row.QuantityDelta, row.Reason)).ToArray(), page, pageSize, total));
    }

    private static async Task<IResult?> ValidateAsync(
        PlatformAccessDbContext db, Guid companyId, int page, int pageSize,
        Guid? branchId, Guid? terminalId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            branchId == Guid.Empty || terminalId == Guid.Empty ||
            (terminalId.HasValue && !branchId.HasValue) ||
            (fromUtc.HasValue && toUtc.HasValue && fromUtc >= toUtc))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (branchId.HasValue && !await db.Branches.AnyAsync(branch => branch.Id == branchId &&
                branch.CompanyId == companyId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        if (terminalId.HasValue && !await db.PosTerminals.AnyAsync(terminal =>
                terminal.Id == terminalId && terminal.CompanyId == companyId &&
                terminal.BranchId == branchId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "TERMINAL_NOT_FOUND");
        return null;
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

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record HistoryPage<T>(T[] Items, int Page, int PageSize, long TotalItems);
    private sealed record BusinessSummary(DateTimeOffset FromUtc, DateTimeOffset ToUtc, Guid? BranchId,
        int SaleCount, decimal SalesTotal, decimal ImmediateSalePayments,
        PaymentSummary[] PaymentsByMethod, decimal NewAccountCharges, decimal RegisteredAccountCharges,
        decimal CreditApplied, decimal OutstandingDebt, decimal CompanyCreditAvailable,
        decimal CollectionsReceived, decimal CollectionsRefunded, decimal CashCollectionsNet,
        decimal NonCashCollectionsNet, int OpenShiftCount, decimal OpenShiftCashBalance);
    private sealed record PaymentSummary(string Method, decimal Amount);
    private sealed record SaleItem(Guid Id, DateTimeOffset ConfirmedAtUtc, decimal Total,
        Guid BranchId, string BranchName, Guid? TerminalId, string? TerminalName,
        Guid CashierId, string CashierName, Guid ShiftId);
    private sealed record SaleDetail(Guid Id, DateTimeOffset ConfirmedAtUtc, decimal Total,
        decimal DiscountAmount, string? DiscountReason,
        SaleLineItem[] Lines, SalePaymentItem[] Payments);
    private sealed record SaleLineItem(string Code, string Name, string Unit, string SaleMode,
        decimal Quantity, decimal UnitPrice, decimal LineTotal);
    private sealed record SalePaymentItem(string Method, decimal TenderedAmount, decimal AppliedAmount);
    private sealed record ShiftRow(Guid Id, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc,
        CashierShiftStatus Status, decimal OpeningCash, Guid BranchId, string BranchName,
        Guid? TerminalId, string? TerminalName, Guid CashierId, string CashierName);
    private sealed record ShiftItem(Guid Id, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc,
        string Status, decimal OpeningCash, Guid BranchId, string BranchName,
        Guid? TerminalId, string? TerminalName, Guid CashierId, string CashierName);
    private sealed record CashRow(Guid Id, DateTimeOffset CreatedAtUtc, Guid BranchId, string BranchName,
        Guid? TerminalId, string? TerminalName, Guid CashierId, string CashierName,
        Guid ShiftId, Guid? SaleId, CashLedgerMovementKind Kind, PaymentMethod Method, decimal AmountDelta);
    private sealed record CashItem(Guid Id, DateTimeOffset CreatedAtUtc, Guid BranchId, string BranchName,
        Guid? TerminalId, string? TerminalName, Guid CashierId, string CashierName,
        Guid ShiftId, Guid? SaleId, string Kind, string Method, decimal AmountDelta);
    private sealed record StockRow(Guid Id, DateTimeOffset CreatedAtUtc, Guid BranchId, string BranchName,
        Guid ProductId, string ProductName, Guid UserId, string Username,
        InventoryMovementKind Kind, decimal QuantityDelta, string Reason);
    private sealed record StockItem(Guid Id, DateTimeOffset CreatedAtUtc, Guid BranchId, string BranchName,
        Guid ProductId, string ProductName, Guid UserId, string Username,
        string Kind, decimal QuantityDelta, string Reason);
}
