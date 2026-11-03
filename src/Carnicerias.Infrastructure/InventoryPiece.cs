using System.Text;

namespace Carnicerias.Infrastructure;

public enum InventoryPieceStatus
{
    Available,
    Reserved,
    Sold,
    Transferred,
    Consumed,
    HistoricalUnverified,
    ReconciledUnavailable
}

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
        Status = InventoryPieceStatus.Available;
        StateChangedAtUtc = ReceivedAtUtc;
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
    public InventoryPieceStatus Status { get; private set; }
    public Guid? ReservedBySaleDraftId { get; private set; }
    public DateTimeOffset? ReservedAtUtc { get; private set; }
    public DateTimeOffset StateChangedAtUtc { get; private set; }

    public void ReserveForSale(Guid draftId, DateTimeOffset now)
    {
        if (draftId == Guid.Empty) throw new ArgumentException("A draft is required.", nameof(draftId));
        if (Status == InventoryPieceStatus.Reserved && ReservedBySaleDraftId == draftId) return;
        if (Status != InventoryPieceStatus.Available)
            throw new InvalidOperationException("The piece is not available for a sale draft.");
        Status = InventoryPieceStatus.Reserved;
        ReservedBySaleDraftId = draftId;
        ReservedAtUtc = now.ToUniversalTime();
        StateChangedAtUtc = ReservedAtUtc.Value;
    }

    public void ReleaseSaleReservation(Guid draftId, DateTimeOffset now)
    {
        if (Status != InventoryPieceStatus.Reserved || ReservedBySaleDraftId != draftId ||
            draftId == Guid.Empty)
            throw new InvalidOperationException("The draft does not own this piece.");
        Status = InventoryPieceStatus.Available;
        ReservedBySaleDraftId = null;
        ReservedAtUtc = null;
        StateChangedAtUtc = now.ToUniversalTime();
    }

    public void MarkSold(Guid draftId, DateTimeOffset now)
    {
        if (Status != InventoryPieceStatus.Reserved || ReservedBySaleDraftId != draftId ||
            draftId == Guid.Empty)
            throw new InvalidOperationException("The draft does not own this piece.");
        Status = InventoryPieceStatus.Sold;
        ReservedBySaleDraftId = null;
        ReservedAtUtc = null;
        StateChangedAtUtc = now.ToUniversalTime();
    }

    public void MarkTransferred(DateTimeOffset now)
    {
        if (Status != InventoryPieceStatus.Available)
            throw new InvalidOperationException("Only an available piece can be transferred.");
        Status = InventoryPieceStatus.Transferred;
        StateChangedAtUtc = now.ToUniversalTime();
    }

    public void MarkConsumed(DateTimeOffset now)
    {
        if (Status != InventoryPieceStatus.Available)
            throw new InvalidOperationException("Only an available piece can be consumed.");
        Status = InventoryPieceStatus.Consumed;
        StateChangedAtUtc = now.ToUniversalTime();
    }

    public void MarkHistoricalUnverified(DateTimeOffset now)
    {
        if (Status != InventoryPieceStatus.Available)
            throw new InvalidOperationException("Only an unclaimed historical piece can be quarantined.");
        Status = InventoryPieceStatus.HistoricalUnverified;
        StateChangedAtUtc = now.ToUniversalTime();
    }

    public void ReconcileHistoricalAvailable(DateTimeOffset now)
    {
        if (Status != InventoryPieceStatus.HistoricalUnverified)
            throw new InvalidOperationException("The piece is not pending historical reconciliation.");
        Status = InventoryPieceStatus.Available;
        StateChangedAtUtc = now.ToUniversalTime();
    }

    public void ReconcileHistoricalUnavailable(DateTimeOffset now)
    {
        if (Status != InventoryPieceStatus.HistoricalUnverified)
            throw new InvalidOperationException("The piece is not pending historical reconciliation.");
        Status = InventoryPieceStatus.ReconciledUnavailable;
        StateChangedAtUtc = now.ToUniversalTime();
    }
}
