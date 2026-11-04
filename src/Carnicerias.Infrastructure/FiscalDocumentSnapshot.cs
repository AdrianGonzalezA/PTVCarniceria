namespace Carnicerias.Infrastructure;

/// <summary>Amounts sent to ARCA for one immutable fiscal numbering attempt.</summary>
public sealed class FiscalDocumentSnapshot
{
    private readonly List<FiscalDocumentVatLine> _vatLines = [];
    private FiscalDocumentSnapshot() { }

    public FiscalDocumentSnapshot(Guid fiscalDocumentId, int receiverVatConditionCode,
        decimal total, decimal taxableBase, decimal exemptAmount, decimal notTaxedAmount,
        IReadOnlyList<FiscalDocumentVatLine> vatLines)
    {
        ArgumentNullException.ThrowIfNull(vatLines);
        if (fiscalDocumentId == Guid.Empty || receiverVatConditionCode <= 0 || total <= 0 ||
            !Money(total) || !Money(taxableBase) || !Money(exemptAmount) ||
            !Money(notTaxedAmount) || vatLines.Count > 20 ||
            vatLines.Any(line => line.FiscalDocumentId != fiscalDocumentId) ||
            vatLines.Select(line => line.ArcaRateCode).Distinct().Count() != vatLines.Count ||
            vatLines.Sum(line => line.TaxableBase) != taxableBase ||
            taxableBase + exemptAmount + notTaxedAmount +
            vatLines.Sum(line => line.TaxAmount) != total)
            throw new ArgumentException("Fiscal snapshot must match the exact balanced request.");
        FiscalDocumentId = fiscalDocumentId;
        ReceiverVatConditionCode = receiverVatConditionCode;
        Total = total;
        TaxableBase = taxableBase;
        VatAmount = vatLines.Sum(line => line.TaxAmount);
        ExemptAmount = exemptAmount;
        NotTaxedAmount = notTaxedAmount;
        _vatLines.AddRange(vatLines);
    }

    public Guid FiscalDocumentId { get; private set; }
    public int ReceiverVatConditionCode { get; private set; }
    public decimal Total { get; private set; }
    public decimal TaxableBase { get; private set; }
    public decimal VatAmount { get; private set; }
    public decimal ExemptAmount { get; private set; }
    public decimal NotTaxedAmount { get; private set; }
    public IReadOnlyCollection<FiscalDocumentVatLine> VatLines => _vatLines;

    private static bool Money(decimal amount) => amount >= 0 && amount <= 9_999_999_999.99m &&
        decimal.Round(amount, 2) == amount;
}

public sealed class FiscalDocumentVatLine
{
    private FiscalDocumentVatLine() { }

    public FiscalDocumentVatLine(Guid fiscalDocumentId, int arcaRateCode, decimal ratePercent,
        decimal taxableBase, decimal taxAmount)
    {
        if (fiscalDocumentId == Guid.Empty || arcaRateCode <= 0 ||
            ratePercent is < 0 or > 100 || decimal.Round(ratePercent, 2) != ratePercent ||
            taxableBase <= 0 || decimal.Round(taxableBase, 2) != taxableBase ||
            taxAmount < 0 || decimal.Round(taxAmount, 2) != taxAmount)
            throw new ArgumentException("A valid ARCA VAT line is required.");
        FiscalDocumentId = fiscalDocumentId;
        ArcaRateCode = arcaRateCode;
        RatePercent = ratePercent;
        TaxableBase = taxableBase;
        TaxAmount = taxAmount;
    }

    public Guid FiscalDocumentId { get; private set; }
    public int ArcaRateCode { get; private set; }
    public decimal RatePercent { get; private set; }
    public decimal TaxableBase { get; private set; }
    public decimal TaxAmount { get; private set; }
}
