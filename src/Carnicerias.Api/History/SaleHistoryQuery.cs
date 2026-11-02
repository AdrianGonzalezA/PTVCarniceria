using Carnicerias.Infrastructure;

namespace Carnicerias.Api.History;

public static class SaleHistoryQuery
{
    public static bool TryParseDocumentType(string? value, out SaleDocumentType? documentType)
    {
        documentType = value switch
        {
            "nonFiscalTicket" => SaleDocumentType.NonFiscalTicket,
            "fiscalTicket" => SaleDocumentType.FiscalTicket,
            "electronicInvoice" => SaleDocumentType.ElectronicInvoice,
            _ => null
        };
        return value is null || documentType.HasValue;
    }

    public static IQueryable<ConfirmedSale> ScopeSales(IQueryable<ConfirmedSale> sales,
        Guid companyId, Guid? branchId, Guid? terminalId, DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc, SaleDocumentType? documentType)
    {
        var query = sales.Where(sale => sale.CompanyId == companyId &&
            (!branchId.HasValue || sale.BranchId == branchId.Value) &&
            (!terminalId.HasValue || sale.PosTerminalId == terminalId.Value) &&
            (!fromUtc.HasValue || sale.ConfirmedAtUtc >= fromUtc.Value) &&
            (!toUtc.HasValue || sale.ConfirmedAtUtc < toUtc.Value));
        if (documentType is { } selected)
            query = query.Where(sale => sale.DocumentType == selected);
        return query;
    }

    public static IQueryable<ConfirmedSale> PageSales(IQueryable<ConfirmedSale> sales,
        int page, int pageSize) =>
        sales.OrderByDescending(sale => sale.ConfirmedAtUtc).ThenByDescending(sale => sale.Id)
            .Skip((page - 1) * pageSize).Take(pageSize);
}
