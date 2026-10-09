using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Fiscal;

public sealed class FiscalSaleNotReadyException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Builds a WSFE request solely from the sale's immutable tax snapshot.</summary>
public static class FiscalRequestBuilder
{
    public static ArcaCaeRequest Build(ConfirmedSale sale, int pointOfSale,
        long number, DateOnly issueDate)
    {
        if (sale.DocumentType == SaleDocumentType.NonFiscalTicket)
            throw new FiscalSaleNotReadyException("NOT_FISCAL_SALE");
        if (sale.Lines.Count == 0 || sale.Lines.Any(line => line.TaxRuleId is null ||
                line.TaxTreatment is null || line.NetAfterDiscount is null ||
                line.TaxableBase is null || line.TaxAmount is null) ||
            sale.Lines.Sum(line => line.NetAfterDiscount) != sale.Total)
            throw new FiscalSaleNotReadyException("INCOMPLETE_TAX_SNAPSHOT");

        // This first homologation path models a registered-VAT issuer only.
        // Source: https://www.arca.gob.ar/facturacion/regimen-general/comprobantes.asp
        var voucherType = sale.RecipientTaxStatus is
            SaleRecipientTaxStatus.Registered or SaleRecipientTaxStatus.SmallTaxpayer ? 1 : 6;
        var conditionCode = sale.RecipientTaxStatus switch
        {
            SaleRecipientTaxStatus.Registered => 1,
            SaleRecipientTaxStatus.Exempt => 4,
            SaleRecipientTaxStatus.FinalConsumer => 5,
            SaleRecipientTaxStatus.SmallTaxpayer => 6,
            _ => throw new FiscalSaleNotReadyException("UNSUPPORTED_RECIPIENT_STATUS")
        };
        var document = sale.RecipientDocumentNumber;
        var documentType = document is null ? 99 : document.Length == 11 ? 80 : 96;
        if (documentType == 99 && sale.RecipientTaxStatus != SaleRecipientTaxStatus.FinalConsumer)
            throw new FiscalSaleNotReadyException("RECIPIENT_ID_REQUIRED");
        if (!long.TryParse(document, out var documentNumber) && document is not null)
            throw new FiscalSaleNotReadyException("RECIPIENT_ID_INVALID");

        var taxable = sale.Lines.Where(line => line.TaxTreatment == SaleTaxTreatment.Taxed).ToArray();
        var vat = taxable.GroupBy(line => line.TaxRatePercent).Select(group =>
        {
            // WSFEv1 example maps 0/10.5/21/27% to 3/4/5/6 respectively.
            // Source: https://www.arca.gob.ar/fe/ayuda/documentos/wsfev1-RG-4291.pdf
            var rateCode = group.Key switch
            {
                0m => 3,
                10.5m => 4,
                21m => 5,
                27m => 6,
                _ => throw new FiscalSaleNotReadyException("UNMAPPED_VAT_RATE")
            };
            return new ArcaVatAmount(rateCode,
                group.Sum(line => line.TaxableBase!.Value),
                group.Sum(line => line.TaxAmount!.Value));
        }).ToArray();
        var taxableBase = vat.Sum(item => item.TaxableBase);
        var exempt = sale.Lines.Where(line => line.TaxTreatment == SaleTaxTreatment.Exempt)
            .Sum(line => line.NetAfterDiscount!.Value);
        var notTaxed = sale.Lines.Where(line => line.TaxTreatment == SaleTaxTreatment.NotTaxed)
            .Sum(line => line.NetAfterDiscount!.Value);
        if (taxableBase + vat.Sum(item => item.TaxAmount) + exempt + notTaxed != sale.Total)
            throw new FiscalSaleNotReadyException("TAX_TOTAL_MISMATCH");

        return new ArcaCaeRequest(pointOfSale, voucherType, number, issueDate,
            documentType, documentNumber, conditionCode, sale.Total, taxableBase,
            exempt, notTaxed, vat);
    }
}
