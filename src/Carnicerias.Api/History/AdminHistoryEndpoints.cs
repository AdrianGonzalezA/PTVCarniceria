using Carnicerias.Api.Contracts;
using Carnicerias.Api.Fiscal;
using Carnicerias.Api.Inventory;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using System.Text.Json;

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
        history.MapGet("/shifts/{shiftId:guid}/report", GetShiftReportAsync);
        history.MapGet("/shifts/{shiftId:guid}/report.pdf", GetShiftReportPdfAsync);
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
                 item.Kind == CashLedgerMovementKind.AccountCollectionRefund ||
                 item.Kind == CashLedgerMovementKind.AccountCollectionChange))
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Received = group.Where(item => item.Kind == CashLedgerMovementKind.AccountCollection ||
                    item.Kind == CashLedgerMovementKind.AccountCollectionChange)
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
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? documentType = null)
    {
        if (!SaleHistoryQuery.TryParseDocumentType(documentType, out var parsedDocumentType))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var check = await ValidateAsync(db, accessor.Context.CompanyId, page, pageSize,
            branchId, terminalId, fromUtc, toUtc, cancellationToken);
        if (check is not null) return check;
        var companyId = accessor.Context.CompanyId;
        var query = SaleHistoryQuery.ScopeSales(db.ConfirmedSales.AsNoTracking(), companyId,
            branchId, terminalId, fromUtc, toUtc, parsedDocumentType);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await SaleHistoryQuery.PageSales(query, page, pageSize)
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
        var fiscal = await db.FiscalDocuments.AsNoTracking()
            .Include(document => document.Snapshot)
            .ThenInclude(snapshot => snapshot!.VatLines)
            .Where(document => document.CompanyId == sale.CompanyId &&
                document.SaleId == sale.Id)
            .OrderByDescending(document => document.CreatedAtUtc)
            .ThenByDescending(document => document.Number)
            .FirstOrDefaultAsync(cancellationToken);
        return Results.Ok(new SaleDetail(
            sale.Id,
            sale.ConfirmedAtUtc,
            sale.Total,
            sale.DiscountAmount,
            sale.DiscountReason,
            sale.DocumentType switch
            {
                SaleDocumentType.FiscalTicket => "fiscalTicket",
                SaleDocumentType.ElectronicInvoice => "electronicInvoice",
                _ => "nonFiscalTicket"
            },
            sale.RecipientTaxStatus switch
            {
                SaleRecipientTaxStatus.Registered => "registered",
                SaleRecipientTaxStatus.SmallTaxpayer => "smallTaxpayer",
                SaleRecipientTaxStatus.Exempt => "exempt",
                _ => "finalConsumer"
            },
            sale.RecipientName, sale.RecipientDocumentNumber, sale.RecipientAddress,
            sale.RecipientLocality, sale.RecipientProvince, sale.FiscalCustomerCode,
            sale.PurchaseOrderNumber,
            sale.Lines.Any(line => line.NetAfterDiscount is null) ? "legacy" :
                sale.Lines.Any(line => line.TaxRuleId is null) ? "unconfigured" : "complete",
            sale.Lines.All(line => line.TaxRuleId is not null)
                ? sale.Lines.Sum(line => line.TaxableBase ?? 0m) : null,
            sale.Lines.All(line => line.TaxRuleId is not null)
                ? sale.Lines.Sum(line => line.TaxAmount ?? 0m) : null,
            sale.Lines.All(line => line.TaxRuleId is not null)
                ? sale.Lines.Where(line => line.TaxTreatment == Carnicerias.Domain.Sales.SaleTaxTreatment.Exempt)
                    .Sum(line => line.NetAfterDiscount ?? 0m) : null,
            sale.Lines.All(line => line.TaxRuleId is not null)
                ? sale.Lines.Where(line => line.TaxTreatment == Carnicerias.Domain.Sales.SaleTaxTreatment.NotTaxed)
                    .Sum(line => line.NetAfterDiscount ?? 0m) : null,
            sale.Lines.OrderBy(line => line.ProductName).Select(line => new SaleLineItem(
                line.ProductCode, line.ProductName, line.Unit,
                line.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
                line.Quantity, line.UnitPrice, line.LineTotal,
                line.PieceIdentifier, line.PieceCount,
                line.OrderDiscountAmount, line.NetAfterDiscount, line.TaxTreatment?.ToString(),
                line.TaxRatePercent, line.TaxableBase, line.TaxAmount)).ToArray(),
            sale.Payments.OrderBy(payment => payment.Method).Select(payment => new SalePaymentItem(
                PaymentMethodName(payment.Method), payment.TenderedAmount, payment.AppliedAmount)).ToArray(),
            fiscal is null ? null : new FiscalDocumentDetail(
                fiscal.Id, fiscal.Status.ToString(), fiscal.PointOfSale,
                fiscal.VoucherType, fiscal.Number, fiscal.IssueDate, fiscal.Total,
                fiscal.Cae, FiscalDocumentEndpoints.SnapshotStatus(fiscal),
                fiscal.Snapshot?.TaxableBase,
                fiscal.Snapshot?.VatAmount,
                fiscal.Snapshot?.ExemptAmount, fiscal.Snapshot?.NotTaxedAmount,
                fiscal.Snapshot?.VatLines.OrderBy(line => line.ArcaRateCode)
                    .Select(line => new FiscalVatLineDetail(line.ArcaRateCode,
                        line.RatePercent, line.TaxableBase, line.TaxAmount)).ToArray())));
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
                    .Select(user => user.Username).FirstOrDefault() ?? "",
                db.ShiftClosingReports.Any(report => report.CompanyId == shift.CompanyId &&
                    report.CashierShiftId == shift.Id)))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new HistoryPage<ShiftItem>(rows.Select(row => new ShiftItem(
            row.Id, row.OpenedAtUtc, row.ClosedAtUtc,
            row.Status == CashierShiftStatus.Open ? "open" : "closed", row.OpeningCash,
            row.BranchId, row.BranchName, row.TerminalId, row.TerminalName,
            row.CashierId, row.CashierName, row.HasReport)).ToArray(), page, pageSize, total));
    }

    private static async Task<IResult> GetShiftReportAsync(Guid shiftId,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var report = await LoadShiftReportAsync(shiftId, db, accessor.Context.CompanyId, cancellationToken);
        return report is null ? Results.NotFound() : Results.Ok(report);
    }

    private static async Task<IResult> GetShiftReportPdfAsync(Guid shiftId,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var report = await LoadShiftReportAsync(shiftId, db, accessor.Context.CompanyId, cancellationToken);
        if (report is null) return Results.NotFound();
        return Results.File(await ShiftClosingPdf.RenderAsync(report), "application/pdf",
            $"rendicion-turno-{shiftId:N}.pdf");
    }

    private static async Task<ShiftClosingSnapshot?> LoadShiftReportAsync(Guid shiftId,
        PlatformAccessDbContext db, Guid companyId, CancellationToken cancellationToken)
    {
        var json = await db.ShiftClosingReports.AsNoTracking()
            .Where(report => report.CompanyId == companyId && report.CashierShiftId == shiftId)
            .Select(report => report.SnapshotJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<ShiftClosingSnapshot>(
            json, ShiftClosingSnapshot.JsonOptions);
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
                CashLedgerMovementKind.AccountCollectionChange => "accountCollectionChange",
                CashLedgerMovementKind.ExtraordinaryWithdrawal => "extraordinaryWithdrawal",
                _ => "unknown"
            }, PaymentMethodName(row.Method), row.AmountDelta)).ToArray(), page, pageSize, total));
    }

    internal static async Task<IResult> ListStockMovementsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, int pageSize = 30, Guid? branchId = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null,
        Guid? productId = null, Guid? categoryId = null, string? productSearch = null,
        string? categorySearch = null, string? kind = null)
    {
        var check = await ValidateAsync(db, accessor.Context.CompanyId, page, pageSize,
            branchId, null, fromUtc, toUtc, cancellationToken);
        if (check is not null) return check;
        var companyId = accessor.Context.CompanyId;
        var search = productSearch?.Trim();
        var categoryTerm = categorySearch?.Trim();
        if (productId == Guid.Empty || categoryId == Guid.Empty || search?.Length > 80 ||
            categoryTerm?.Length > 80 ||
            !StockMovementKindCode.TryParse(kind, out var selectedKind))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        await using var snapshot = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        if (productId.HasValue && !await db.CatalogProducts.AsNoTracking().AnyAsync(product =>
                product.CompanyId == companyId && product.Id == productId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PRODUCT_NOT_FOUND");
        if (categoryId.HasValue && !await db.ProductCategories.AsNoTracking().AnyAsync(category =>
                category.CompanyId == companyId && category.Id == categoryId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "CATEGORY_NOT_FOUND");
        var query = db.InventoryMovements.AsNoTracking().Where(item => item.CompanyId == companyId &&
            (!branchId.HasValue || item.BranchId == branchId.Value) &&
            (!fromUtc.HasValue || item.CreatedAtUtc >= fromUtc.Value) &&
            (!toUtc.HasValue || item.CreatedAtUtc < toUtc.Value));
        if (productId.HasValue) query = query.Where(item => item.ProductId == productId.Value);
        if (categoryId.HasValue) query = query.Where(item => db.CatalogProducts.Any(product =>
            product.CompanyId == companyId && product.Id == item.ProductId &&
            product.CategoryId == categoryId.Value));
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = StockMovementKindCode.SearchPattern(search);
            query = query.Where(item => db.CatalogProducts.Any(product =>
                product.CompanyId == companyId && product.Id == item.ProductId &&
                (EF.Functions.ILike(product.Name, pattern, "\\") ||
                 EF.Functions.ILike(product.Code, pattern, "\\"))));
        }
        if (!string.IsNullOrEmpty(categoryTerm))
        {
            var pattern = StockMovementKindCode.SearchPattern(categoryTerm);
            query = query.Where(item => db.CatalogProducts.Any(product =>
                product.CompanyId == companyId && product.Id == item.ProductId &&
                db.ProductCategories.Any(category => category.CompanyId == companyId &&
                    category.Id == product.CategoryId &&
                    EF.Functions.ILike(category.Name, pattern, "\\"))));
        }
        if (selectedKind.HasValue) query = query.Where(item => item.Kind == selectedKind.Value);
        var total = await query.LongCountAsync(cancellationToken);
        Guid? summaryProductId = productId;
        if (branchId.HasValue && !summaryProductId.HasValue && !string.IsNullOrEmpty(search))
        {
            var matches = await query.Select(item => item.ProductId).Distinct().Take(2)
                .ToArrayAsync(cancellationToken);
            if (matches.Length == 1) summaryProductId = matches[0];
        }
        StockSummary? summary = null;
        if (branchId.HasValue && summaryProductId.HasValue)
        {
            var summaryProduct = await db.CatalogProducts.AsNoTracking()
                .Where(product => product.CompanyId == companyId &&
                    product.Id == summaryProductId.Value)
                .Select(product => new { product.Id, product.Code, product.Name,
                    product.Unit, product.SaleMode }).SingleAsync(cancellationToken);
            var ledger = db.InventoryMovements.AsNoTracking().Where(item =>
                item.CompanyId == companyId && item.BranchId == branchId.Value &&
                item.ProductId == summaryProductId.Value);
            var opening = fromUtc.HasValue
                ? await ledger.Where(item => item.CreatedAtUtc < fromUtc.Value)
                    .SumAsync(item => (decimal?)item.QuantityDelta, cancellationToken) ?? 0
                : 0;
            var totals = await ledger.Where(item =>
                    (!fromUtc.HasValue || item.CreatedAtUtc >= fromUtc.Value) &&
                    (!toUtc.HasValue || item.CreatedAtUtc < toUtc.Value))
                .GroupBy(item => item.Kind)
                .Select(group => new { Kind = group.Key,
                    Quantity = group.Sum(item => item.QuantityDelta) })
                .ToDictionaryAsync(item => item.Kind, item => item.Quantity, cancellationToken);
            decimal Quantity(InventoryMovementKind movementKind) =>
                totals.GetValueOrDefault(movementKind);
            summary = new StockSummary(summaryProduct.Id, summaryProduct.Code,
                summaryProduct.Name, summaryProduct.Unit,
                summaryProduct.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
                opening, Quantity(InventoryMovementKind.OpeningBalance),
                Quantity(InventoryMovementKind.Receipt),
                Quantity(InventoryMovementKind.PieceReceipt),
                Quantity(InventoryMovementKind.Sale),
                Quantity(InventoryMovementKind.TransferOut),
                Quantity(InventoryMovementKind.TransferIn),
                Quantity(InventoryMovementKind.Adjustment),
                Quantity(InventoryMovementKind.PhysicalCountAdjustment),
                Quantity(InventoryMovementKind.ProductionInput),
                Quantity(InventoryMovementKind.ProductionOutput),
                opening + totals.Values.Sum());
        }
        var rows = await query.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new StockRow(item.Id, item.CreatedAtUtc, item.BranchId,
                db.Branches.Where(branch => branch.Id == item.BranchId)
                    .Select(branch => branch.Name).FirstOrDefault() ?? "",
                item.ProductId, db.CatalogProducts.Where(product => product.Id == item.ProductId)
                    .Select(product => product.Name).FirstOrDefault() ?? "",
                db.CatalogProducts.Where(product => product.Id == item.ProductId)
                    .Select(product => product.SaleMode).First(),
                db.CatalogProducts.Where(product => product.Id == item.ProductId)
                    .Select(product => product.Unit).First(),
                item.UserId, db.Users.Where(user => user.Id == item.UserId)
                    .Select(user => user.Username).FirstOrDefault() ?? "",
                item.Kind, item.QuantityDelta, item.Reason, item.OperationId))
            .ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var balances = ids.Length == 0 ? new Dictionary<Guid, decimal>() :
            (await db.Database.SqlQueryRaw<StockBalanceRow>("""
                SELECT current."Id" AS "Id",
                    COALESCE((SELECT SUM(prior."QuantityDelta")
                        FROM inventory.inventory_movements AS prior
                        WHERE prior."CompanyId" = current."CompanyId"
                            AND prior."BranchId" = current."BranchId"
                            AND prior."ProductId" = current."ProductId"
                            AND (prior."CreatedAtUtc", prior."Id") <=
                                (current."CreatedAtUtc", current."Id")), 0) AS "RunningBalance"
                FROM inventory.inventory_movements AS current
                WHERE current."CompanyId" = @companyId AND current."Id" = ANY(@movementIds)
                """, new NpgsqlParameter("companyId", companyId),
                new NpgsqlParameter("movementIds", ids)).ToArrayAsync(cancellationToken))
            .ToDictionary(row => row.Id, row => row.RunningBalance);
        var receipts = await db.InventoryReceiptLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.InventoryMovementId.HasValue &&
                ids.Contains(line.InventoryMovementId.Value))
            .Select(line => new StockDocumentRow(line.InventoryMovementId!.Value, "receipt",
                line.ReceiptId, db.InventoryReceipts.Where(document =>
                    document.CompanyId == companyId && document.Id == line.ReceiptId)
                    .Select(document => document.DocumentType + " " + document.DocumentNumber)
                    .FirstOrDefault() ?? ""))
            .ToArrayAsync(cancellationToken);
        var transfers = await db.InventoryTransferLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.InventoryMovementId.HasValue &&
                ids.Contains(line.InventoryMovementId.Value))
            .Select(line => new StockDocumentRow(line.InventoryMovementId!.Value, "transfer",
                line.TransferId, db.InventoryTransfers.Where(document =>
                    document.CompanyId == companyId && document.Id == line.TransferId)
                    .Select(document => document.DocumentNumber).FirstOrDefault() ?? ""))
            .ToArrayAsync(cancellationToken);
        var transferReceipts = await db.InventoryTransferReceiptLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.InventoryMovementId.HasValue &&
                ids.Contains(line.InventoryMovementId.Value))
            .Select(line => new StockDocumentRow(line.InventoryMovementId!.Value,
                "transferReceipt", line.ReceiptId,
                db.InventoryTransferReceipts.Where(document =>
                    document.CompanyId == companyId && document.Id == line.ReceiptId)
                    .Select(document => document.DocumentNumber).FirstOrDefault() ?? ""))
            .ToArrayAsync(cancellationToken);
        var counts = await db.PhysicalStockCountLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.InventoryMovementId.HasValue &&
                ids.Contains(line.InventoryMovementId.Value))
            .Select(line => new StockDocumentRow(line.InventoryMovementId!.Value,
                "physicalCount", line.CountId, line.CountId.ToString()))
            .ToArrayAsync(cancellationToken);
        var production = await db.ProductionMaterialLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.InventoryMovementId.HasValue &&
                ids.Contains(line.InventoryMovementId.Value))
            .Select(line => new StockDocumentRow(line.InventoryMovementId!.Value,
                "production", line.OrderId, line.OrderId.ToString()))
            .ToArrayAsync(cancellationToken);
        var documents = receipts.Concat(transfers).Concat(transferReceipts)
            .Concat(counts).Concat(production)
            .ToDictionary(row => row.MovementId);
        var result = new StockHistoryPage(rows.Select(row => new StockItem(
            row.Id, row.CreatedAtUtc, row.BranchId, row.BranchName, row.ProductId,
            row.ProductName, row.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
            row.Unit,
            row.UserId, row.Username, StockMovementKindCode.Format(row.Kind),
            row.QuantityDelta, row.Reason, row.OperationId,
            balances[row.Id], documents.TryGetValue(row.Id, out var document)
                ? document.Kind : row.Kind == InventoryMovementKind.Sale ? "sale" : null,
            document?.DocumentId ?? StockMovementKindCode.SaleId(row.Kind, row.Reason),
            document?.DocumentNumber ?? StockMovementKindCode.SaleNumber(row.Kind, row.Reason)))
            .ToArray(), page, pageSize, total, summary);
        await snapshot.CommitAsync(cancellationToken);
        return Results.Ok(result);
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
        string DocumentType, string RecipientTaxStatus, string? RecipientName,
        string? RecipientDocumentNumber, string? RecipientAddress,
        string? RecipientLocality, string? RecipientProvince, string? FiscalCustomerCode,
        string? PurchaseOrderNumber,
        string TaxSnapshotStatus, decimal? TaxableBase, decimal? TaxAmount,
        decimal? ExemptAmount, decimal? NotTaxedAmount,
        SaleLineItem[] Lines, SalePaymentItem[] Payments,
        FiscalDocumentDetail? FiscalDocument);
    private sealed record FiscalDocumentDetail(Guid Id, string Status, int PointOfSale,
        int VoucherType, long Number, DateOnly IssueDate, decimal Total, string? Cae,
        string SnapshotStatus, decimal? TaxableBase, decimal? TaxAmount,
        decimal? ExemptAmount, decimal? NotTaxedAmount, FiscalVatLineDetail[]? VatLines);
    private sealed record FiscalVatLineDetail(int ArcaRateCode, decimal RatePercent,
        decimal TaxableBase, decimal TaxAmount);
    private sealed record SaleLineItem(string Code, string Name, string Unit, string SaleMode,
        decimal Quantity, decimal UnitPrice, decimal LineTotal,
        string? PieceIdentifier, int? PieceCount,
        decimal OrderDiscountAmount, decimal? NetAfterDiscount, string? TaxTreatment,
        decimal? TaxRatePercent, decimal? TaxableBase, decimal? TaxAmount);
    private sealed record SalePaymentItem(string Method, decimal TenderedAmount, decimal AppliedAmount);
    private sealed record ShiftRow(Guid Id, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc,
        CashierShiftStatus Status, decimal OpeningCash, Guid BranchId, string BranchName,
        Guid? TerminalId, string? TerminalName, Guid CashierId, string CashierName, bool HasReport);
    private sealed record ShiftItem(Guid Id, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc,
        string Status, decimal OpeningCash, Guid BranchId, string BranchName,
        Guid? TerminalId, string? TerminalName, Guid CashierId, string CashierName, bool HasReport);
    private sealed record CashRow(Guid Id, DateTimeOffset CreatedAtUtc, Guid BranchId, string BranchName,
        Guid? TerminalId, string? TerminalName, Guid CashierId, string CashierName,
        Guid ShiftId, Guid? SaleId, CashLedgerMovementKind Kind, PaymentMethod Method, decimal AmountDelta);
    private sealed record CashItem(Guid Id, DateTimeOffset CreatedAtUtc, Guid BranchId, string BranchName,
        Guid? TerminalId, string? TerminalName, Guid CashierId, string CashierName,
        Guid ShiftId, Guid? SaleId, string Kind, string Method, decimal AmountDelta);
    private sealed record StockRow(Guid Id, DateTimeOffset CreatedAtUtc, Guid BranchId, string BranchName,
        Guid ProductId, string ProductName, ProductSaleMode SaleMode, string Unit,
        Guid UserId, string Username,
        InventoryMovementKind Kind, decimal QuantityDelta, string Reason, Guid OperationId);
    private sealed record StockItem(Guid Id, DateTimeOffset CreatedAtUtc, Guid BranchId, string BranchName,
        Guid ProductId, string ProductName, string SaleMode, string Unit,
        Guid UserId, string Username,
        string Kind, decimal QuantityDelta, string Reason, Guid OperationId,
        decimal RunningBalance, string? DocumentKind, Guid? DocumentId,
        string? DocumentNumber);
    private sealed record StockHistoryPage(StockItem[] Items, int Page, int PageSize,
        long TotalItems, StockSummary? Summary);
    private sealed record StockSummary(Guid ProductId, string ProductCode,
        string ProductName, string Unit, string SaleMode, decimal OpeningBalance,
        decimal OpeningMovements, decimal Receipts, decimal PieceReceipts,
        decimal Sales, decimal TransfersOut, decimal TransfersIn, decimal Adjustments,
        decimal PhysicalCountAdjustments,
        decimal ProductionInputs, decimal ProductionOutputs,
        decimal ClosingBalance);
    private sealed class StockBalanceRow
    {
        public Guid Id { get; set; }
        public decimal RunningBalance { get; set; }
    }
    private sealed record StockDocumentRow(Guid MovementId, string Kind,
        Guid DocumentId, string DocumentNumber);
}
