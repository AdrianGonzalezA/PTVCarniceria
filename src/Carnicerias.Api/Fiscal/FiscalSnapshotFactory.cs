using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Fiscal;

public static class FiscalSnapshotFactory
{
    public static FiscalDocumentSnapshot Create(FiscalDocument document, ArcaCaeRequest request)
    {
        if (document.PointOfSale != request.PointOfSale ||
            document.VoucherType != request.VoucherType || document.Number != request.Number ||
            document.IssueDate != request.IssueDate || document.Total != request.Total ||
            document.ReceiverDocumentType != request.ReceiverDocumentType ||
            document.ReceiverDocumentNumber != request.ReceiverDocumentNumber)
            throw new ArgumentException("Fiscal attempt and ARCA request do not match.");
        var lines = request.VatAmounts.Select(amount => new FiscalDocumentVatLine(
            document.Id, amount.ArcaRateCode, RatePercent(amount.ArcaRateCode),
            amount.TaxableBase, amount.TaxAmount)).ToArray();
        return new FiscalDocumentSnapshot(document.Id, request.ReceiverVatConditionCode,
            request.Total, request.TaxableBase, request.ExemptAmount,
            request.NotTaxedAmount, lines);
    }

    private static decimal RatePercent(int arcaRateCode) => arcaRateCode switch
    {
        3 => 0m,
        4 => 10.5m,
        5 => 21m,
        6 => 27m,
        _ => throw new ArgumentException("Unmapped ARCA VAT rate.")
    };
}
