namespace Carnicerias.Infrastructure;

public enum FiscalDocumentStatus { Prepared, NeedsReconciliation, Rejected, Authorized }

/// <summary>An immutable numbering attempt for one sale, separate from payment and stock.</summary>
public sealed class FiscalDocument
{
    private FiscalDocument() => IssuerCuit = string.Empty;

    public FiscalDocument(Guid companyId, Guid saleId, string issuerCuit, int pointOfSale,
        int voucherType, long number, DateOnly issueDate, decimal total,
        int receiverDocumentType, long receiverDocumentNumber, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || saleId == Guid.Empty ||
            issuerCuit.Length != 11 || !issuerCuit.All(char.IsAsciiDigit) ||
            pointOfSale is < 1 or > 99998 || voucherType is not (1 or 6 or 11) ||
            number is < 1 or > 99999999 || issueDate == default ||
            total <= 0 || decimal.Round(total, 2) != total ||
            receiverDocumentType is not (80 or 96 or 99) ||
            receiverDocumentNumber < 0 ||
            (receiverDocumentType == 99 && receiverDocumentNumber != 0) ||
            (receiverDocumentType != 99 && receiverDocumentNumber == 0))
            throw new ArgumentException("A complete fiscal numbering attempt is required.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        SaleId = saleId;
        IssuerCuit = issuerCuit;
        PointOfSale = pointOfSale;
        VoucherType = voucherType;
        Number = number;
        IssueDate = issueDate;
        Total = total;
        ReceiverDocumentType = receiverDocumentType;
        ReceiverDocumentNumber = receiverDocumentNumber;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Status = FiscalDocumentStatus.Prepared;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SaleId { get; private set; }
    public string IssuerCuit { get; private set; }
    public int PointOfSale { get; private set; }
    public int VoucherType { get; private set; }
    public long Number { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public decimal Total { get; private set; }
    public int ReceiverDocumentType { get; private set; }
    public long ReceiverDocumentNumber { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public FiscalDocumentStatus Status { get; private set; }
    public string? Cae { get; private set; }
    public DateOnly? CaeExpiry { get; private set; }
    public DateTimeOffset? AuthorizedAtUtc { get; private set; }
    public string? ErrorCodes { get; private set; }

    public void RequireReconciliation()
    {
        if (Status != FiscalDocumentStatus.Prepared)
            throw new InvalidOperationException("Only a prepared request can become uncertain.");
        Status = FiscalDocumentStatus.NeedsReconciliation;
    }

    public void Reject(IReadOnlyList<int> errorCodes)
    {
        if (Status != FiscalDocumentStatus.Prepared || errorCodes.Count is < 1 or > 20 ||
            errorCodes.Any(code => code is < 1 or > 999999))
            throw new InvalidOperationException("Only a prepared request can be rejected with ARCA codes.");
        ErrorCodes = string.Join(',', errorCodes);
        Status = FiscalDocumentStatus.Rejected;
    }

    public void RejectReconciledCollision()
    {
        if (Status != FiscalDocumentStatus.NeedsReconciliation)
            throw new InvalidOperationException("Only an uncertain request can be reconciled as a collision.");
        ErrorCodes = "10016";
        Status = FiscalDocumentStatus.Rejected;
    }

    public void Authorize(int pointOfSale, int voucherType, long number,
        string cae, DateOnly expiry, DateTimeOffset authorizedAtUtc)
    {
        if (Status is not (FiscalDocumentStatus.Prepared or FiscalDocumentStatus.NeedsReconciliation))
            throw new InvalidOperationException("The fiscal attempt is not awaiting authorization.");
        if (pointOfSale != PointOfSale || voucherType != VoucherType || number != Number ||
            cae.Length != 14 || !cae.All(char.IsAsciiDigit) || expiry < IssueDate)
            throw new ArgumentException("The CAE does not match this fiscal attempt.");
        Cae = cae;
        CaeExpiry = expiry;
        AuthorizedAtUtc = authorizedAtUtc.ToUniversalTime();
        Status = FiscalDocumentStatus.Authorized;
    }
}
