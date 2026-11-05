using System.Text;

namespace Carnicerias.Infrastructure;

/// <summary>A verified barcode read in a draft, without stock impact until approval.</summary>
public sealed class InventoryReceiptPieceRead
{
    private InventoryReceiptPieceRead()
    {
        SourceSystem = NormalizedSourceSystem = IdentifierField = ExternalIdentifier =
            RawBarcode = string.Empty;
    }

    public InventoryReceiptPieceRead(Guid companyId, Guid receiptId, Guid lineId, Guid operationId,
        Guid barcodeProfileId, string sourceSystem, string identifierField,
        string externalIdentifier, string rawBarcode, decimal weightKg)
    {
        if (companyId == Guid.Empty || receiptId == Guid.Empty || lineId == Guid.Empty ||
            operationId == Guid.Empty ||
            barcodeProfileId == Guid.Empty || string.IsNullOrWhiteSpace(sourceSystem) ||
            string.IsNullOrWhiteSpace(identifierField) || string.IsNullOrWhiteSpace(externalIdentifier) ||
            string.IsNullOrWhiteSpace(rawBarcode) || weightKg <= 0 || weightKg > 100_000 ||
            decimal.Round(weightKg, 3) != weightKg)
            throw new ArgumentException("A valid traceable barcode read is required.");
        var normalizedSource = sourceSystem.Trim().Normalize(NormalizationForm.FormKC);
        if (normalizedSource.Length > 120 || identifierField.Trim().Length > 80 ||
            externalIdentifier.Trim().Length > 80 || rawBarcode.Trim().Length > 80)
            throw new ArgumentException("Traceable barcode fields exceed their limits.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        ReceiptId = receiptId;
        LineId = lineId;
        OperationId = operationId;
        BarcodeProfileId = barcodeProfileId;
        SourceSystem = normalizedSource;
        NormalizedSourceSystem = normalizedSource.ToUpperInvariant();
        IdentifierField = identifierField.Trim().ToLowerInvariant();
        ExternalIdentifier = externalIdentifier.Trim();
        RawBarcode = rawBarcode.Trim();
        WeightKg = weightKg;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid LineId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid BarcodeProfileId { get; private set; }
    public string SourceSystem { get; private set; }
    public string NormalizedSourceSystem { get; private set; }
    public string IdentifierField { get; private set; }
    public string ExternalIdentifier { get; private set; }
    public string RawBarcode { get; private set; }
    public decimal WeightKg { get; private set; }
    public Guid? InventoryPieceId { get; private set; }

    public void LinkPiece(Guid pieceId)
    {
        if (pieceId == Guid.Empty || InventoryPieceId is not null)
            throw new InvalidOperationException("A read can be linked to one received piece.");
        InventoryPieceId = pieceId;
    }
}
