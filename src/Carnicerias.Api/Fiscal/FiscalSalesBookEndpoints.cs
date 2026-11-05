using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Fiscal;

public static class FiscalSalesBookEndpoints
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapFiscalSalesBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/fiscal-sales-book")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        group.MapGet("", ListAsync);
        group.MapGet("/export", ExportAsync);
        return endpoints;
    }

    internal static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        DateOnly? from = null, DateOnly? to = null, Guid? branchId = null,
        int page = 1, int pageSize = 30)
    {
        var invalid = await ValidateAsync(db, accessor, from, to, branchId,
            cancellationToken);
        if (invalid is not null) return invalid;
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Error(400, "VALIDATION_ERROR");
        var query = Scope(db, accessor.Context.CompanyId, from!.Value, to!.Value, branchId);
        var completeQuery = query.Where(document => document.Snapshot != null &&
            document.Snapshot.Total == document.Total);
        var complete = await completeQuery.GroupBy(_ => 1).Select(group => new
        {
            Count = group.Count(),
            Total = group.Sum(document => document.Snapshot!.Total),
            TaxableBase = group.Sum(document => document.Snapshot!.TaxableBase),
            VatAmount = group.Sum(document => document.Snapshot!.VatAmount),
            ExemptAmount = group.Sum(document => document.Snapshot!.ExemptAmount),
            NotTaxedAmount = group.Sum(document => document.Snapshot!.NotTaxedAmount)
        }).SingleOrDefaultAsync(cancellationToken);
        var incomplete = await query.Where(document => document.Snapshot == null ||
                document.Snapshot.Total != document.Total)
            .GroupBy(_ => 1).Select(group => new
            {
                Count = group.Count(), KnownTotal = group.Sum(document => document.Total)
            }).SingleOrDefaultAsync(cancellationToken);
        var rateAggregates = await db.FiscalDocumentVatLines.AsNoTracking()
            .Join(completeQuery, line => line.FiscalDocumentId,
                document => document.Id, (line, _) => line)
            .GroupBy(line => new { line.ArcaRateCode, line.RatePercent })
            .Select(group => new
            {
                group.Key.ArcaRateCode,
                group.Key.RatePercent,
                TaxableBase = group.Sum(line => line.TaxableBase),
                TaxAmount = group.Sum(line => line.TaxAmount)
            })
            .OrderBy(item => item.ArcaRateCode).ThenBy(item => item.RatePercent)
            .ToArrayAsync(cancellationToken);
        var rateTotals = rateAggregates.Select(item => new FiscalVatBreakdown(
            item.ArcaRateCode, item.RatePercent, item.TaxableBase,
            item.TaxAmount)).ToArray();
        var documents = await query.OrderBy(document => document.IssueDate)
            .ThenBy(document => document.PointOfSale)
            .ThenBy(document => document.VoucherType)
            .ThenBy(document => document.Number)
            .ThenBy(document => document.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(document => document.Snapshot)
            .ThenInclude(snapshot => snapshot!.VatLines)
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken);
        var items = await ProjectAsync(db, documents, cancellationToken);
        return Results.Ok(new
        {
            from, to, branchId, page, pageSize,
            totalItems = (complete?.Count ?? 0) + (incomplete?.Count ?? 0),
            items,
            totals = new
            {
                completeCount = complete?.Count ?? 0,
                incompleteCount = incomplete?.Count ?? 0,
                completeTotal = complete?.Total ?? 0m,
                incompleteKnownTotal = incomplete?.KnownTotal ?? 0m,
                taxableBase = complete?.TaxableBase ?? 0m,
                vatAmount = complete?.VatAmount ?? 0m,
                exemptAmount = complete?.ExemptAmount ?? 0m,
                notTaxedAmount = complete?.NotTaxedAmount ?? 0m,
                vatBreakdown = rateTotals,
                otherTaxesStatus = "notModeled"
            }
        });
    }

    internal static async Task<IResult> ExportAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        DateOnly? from = null, DateOnly? to = null, Guid? branchId = null)
    {
        var invalid = await ValidateAsync(db, accessor, from, to, branchId,
            cancellationToken);
        if (invalid is not null) return invalid;
        var query = Scope(db, accessor.Context.CompanyId, from!.Value, to!.Value, branchId);
        if (await query.CountAsync(cancellationToken) > 50_000)
            return Error(413, "BOOK_PERIOD_TOO_LARGE");
        var documents = await query.OrderBy(document => document.IssueDate)
            .ThenBy(document => document.PointOfSale)
            .ThenBy(document => document.VoucherType)
            .ThenBy(document => document.Number)
            .ThenBy(document => document.Id)
            .Include(document => document.Snapshot)
            .ThenInclude(snapshot => snapshot!.VatLines)
            .AsSplitQuery().ToArrayAsync(cancellationToken);
        var items = await ProjectAsync(db, documents, cancellationToken);
        var bytes = FiscalSalesBookWorkbook.Create(items);
        return Results.File(bytes, ExcelContentType,
            $"libro-iva-ventas-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }

    private static IQueryable<FiscalDocument> Scope(PlatformAccessDbContext db,
        Guid companyId, DateOnly from, DateOnly to, Guid? branchId)
    {
        var query = db.FiscalDocuments.AsNoTracking().Where(document =>
            document.CompanyId == companyId &&
            document.Status == FiscalDocumentStatus.Authorized &&
            document.IssueDate >= from && document.IssueDate <= to);
        if (branchId.HasValue)
            query = query.Where(document => db.ConfirmedSales.Any(sale =>
                sale.CompanyId == companyId && sale.Id == document.SaleId &&
                sale.BranchId == branchId.Value));
        return query;
    }

    private static async Task<FiscalSalesBookItem[]> ProjectAsync(
        PlatformAccessDbContext db, FiscalDocument[] documents,
        CancellationToken cancellationToken)
    {
        if (documents.Length == 0) return [];
        var saleIds = documents.Select(document => document.SaleId).Distinct().ToArray();
        var sales = await db.ConfirmedSales.AsNoTracking()
            .Where(sale => saleIds.Contains(sale.Id))
            .Select(sale => new { sale.Id, sale.CompanyId, sale.BranchId,
                sale.RecipientName, sale.DocumentType })
            .ToDictionaryAsync(sale => sale.Id, cancellationToken);
        var branchIds = sales.Values.Select(sale => sale.BranchId).Distinct().ToArray();
        var branches = await db.Branches.AsNoTracking()
            .Where(branch => branchIds.Contains(branch.Id))
            .ToDictionaryAsync(branch => branch.Id, branch => branch.Name,
                cancellationToken);
        return documents.Select(document =>
        {
            var sale = sales[document.SaleId];
            var snapshot = document.Snapshot;
            var complete = snapshot is not null && snapshot.Total == document.Total;
            return new FiscalSalesBookItem(document.Id, document.IssueDate,
                branches.GetValueOrDefault(sale.BranchId, ""),
                sale.DocumentType == SaleDocumentType.FiscalTicket
                    ? "fiscalTicket" : "electronicInvoice",
                document.VoucherType, document.PointOfSale, document.Number,
                document.ReceiverDocumentType == 99 ? "0" :
                    document.ReceiverDocumentNumber.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                sale.RecipientName, complete ? snapshot!.ReceiverVatConditionCode : null,
                document.Cae!, complete ? "complete" :
                    snapshot is null ? "legacyIncomplete" : "inconsistent",
                document.Total, complete ? snapshot!.TaxableBase : null,
                complete ? snapshot!.VatAmount : null,
                complete ? snapshot!.ExemptAmount : null,
                complete ? snapshot!.NotTaxedAmount : null,
                complete ? snapshot!.VatLines.OrderBy(line => line.ArcaRateCode)
                    .Select(line => new FiscalVatBreakdown(line.ArcaRateCode,
                        line.RatePercent, line.TaxableBase, line.TaxAmount)).ToArray() : []);
        }).ToArray();
    }

    private static async Task<IResult?> ValidateAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, DateOnly? from, DateOnly? to,
        Guid? branchId, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        if (!context.Permissions.Contains(PlatformPermissionCatalog.UsersManage))
            return Error(403, "ADMIN_REQUIRED");
        if (!from.HasValue || !to.HasValue || to < from ||
            to.Value.DayNumber - from.Value.DayNumber > 366 || branchId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        if (branchId.HasValue && !await db.Branches.AsNoTracking().AnyAsync(branch =>
                branch.CompanyId == context.CompanyId && branch.Id == branchId.Value,
                cancellationToken))
            return Error(404, "BRANCH_NOT_FOUND");
        return null;
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo consultar el Libro IVA", [])),
        statusCode: status);
}
