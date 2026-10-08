using System.Text;

namespace Carnicerias.Infrastructure;

public sealed class InventoryPiece
{
    private InventoryPiece()
    {
        SourceSystem = string.Empty;
        NormalizedSourceSystem = string.Empty;
        ExternalIdentifier = string.Empty;
        RawBarcode = string.Empty;
        IdentifierField = string.Empty;
    }

    public InventoryPiece(Guid companyId, Guid branchId, Guid productId, Guid barcodeProfileId,
        Guid receivedByUserId, Guid operationId, Guid inventoryMovementId, string sourceSystem,
        string externalIdentifier, string rawBarcode, decimal receivedWeightKg,
        DateTimeOffset receivedAtUtc, string identifierField = "pro_identif")
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty ||
            barcodeProfileId == Guid.Empty || receivedByUserId == Guid.Empty || operationId == Guid.Empty ||
            inventoryMovementId == Guid.Empty)
            throw new ArgumentException("Piece receipt identifiers are required.");
        if (string.IsNullOrWhiteSpace(sourceSystem) || sourceSystem.Trim().Length > 120 ||
            string.IsNullOrWhiteSpace(externalIdentifier) || externalIdentifier.Length > 80 ||
            string.IsNullOrWhiteSpace(rawBarcode) || rawBarcode.Length > 80 ||
            string.IsNullOrWhiteSpace(identifierField) || identifierField.Trim().Length > 80)
            throw new ArgumentException("Piece receipt text is invalid.");
        var normalizedSource = sourceSystem.Trim().Normalize(NormalizationForm.FormKC);
        if (normalizedSource.Length > 120)
            throw new ArgumentException("Piece source exceeds the normalized length limit.", nameof(sourceSystem));
        if (receivedWeightKg <= 0 || receivedWeightKg > 100000 ||
            decimal.Round(receivedWeightKg, 3) != receivedWeightKg)
            throw new ArgumentOutOfRangeException(nameof(receivedWeightKg));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        ProductId = productId;
        BarcodeProfileId = barcodeProfileId;
        ReceivedByUserId = receivedByUserId;
        OperationId = operationId;
        InventoryMovementId = inventoryMovementId;
        SourceSystem = normalizedSource;
        NormalizedSourceSystem = SourceSystem.ToUpperInvariant();
        ExternalIdentifier = externalIdentifier.Trim();
        RawBarcode = rawBarcode.Trim();
        IdentifierField = identifierField.Trim().ToLowerInvariant();
        ReceivedWeightKg = receivedWeightKg;
        ReceivedAtUtc = receivedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid BarcodeProfileId { get; private set; }
    public Guid ReceivedByUserId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid InventoryMovementId { get; private set; }
    public string SourceSystem { get; private set; }
    public string NormalizedSourceSystem { get; private set; }
    public string ExternalIdentifier { get; private set; }
    public string RawBarcode { get; private set; }
    public string IdentifierField { get; private set; }
    public decimal ReceivedWeightKg { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
}
