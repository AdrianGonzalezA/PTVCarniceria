namespace Carnicerias.Infrastructure;

public enum InventoryPieceEventKind
{
    Receipt,
    SaleReserved,
    SaleReleased,
    SaleConfirmed,
    AdminReleased,
    MigrationBackfill,
    Transferred,
    Consumed,
    HistoricalReconciled
}

public sealed class InventoryPieceStateEvent
{
    private InventoryPieceStateEvent() => Reason = string.Empty;

    public InventoryPieceStateEvent(Guid companyId, Guid branchId, Guid pieceId,
        InventoryPieceStatus? previousStatus, InventoryPieceStatus status,
        InventoryPieceEventKind kind, Guid? saleDraftId, Guid? actorUserId,
        string reason, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || pieceId == Guid.Empty ||
            saleDraftId == Guid.Empty || actorUserId == Guid.Empty ||
            !Enum.IsDefined(status) || previousStatus is not null &&
            (!Enum.IsDefined(previousStatus.Value) || previousStatus == status) ||
            !Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 240)
            throw new ArgumentException("Invalid piece state event.");

        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        PieceId = pieceId;
        PreviousStatus = previousStatus;
        Status = status;
        Kind = kind;
        SaleDraftId = saleDraftId;
        ActorUserId = actorUserId;
        Reason = reason.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid PieceId { get; private set; }
    public InventoryPieceStatus? PreviousStatus { get; private set; }
    public InventoryPieceStatus Status { get; private set; }
    public InventoryPieceEventKind Kind { get; private set; }
    public Guid? SaleDraftId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? ReconciliationOperationId { get; private set; }
    public string? EvidenceReference { get; private set; }
    public string? EvidenceSha256 { get; private set; }
    public decimal? ObservedWeightKg { get; private set; }
    public decimal? CountedOnHand { get; private set; }
    public DateTimeOffset? CountedAtUtc { get; private set; }
    public Guid? InventoryMovementId { get; private set; }

    public static InventoryPieceStateEvent Reconciled(Guid companyId, Guid branchId, Guid pieceId,
        InventoryPieceStatus outcome, Guid actorUserId, Guid operationId, string reason,
        string evidenceReference, string? evidenceSha256, decimal observedWeightKg,
        decimal countedOnHand, DateTimeOffset countedAtUtc, DateTimeOffset createdAtUtc,
        Guid? inventoryMovementId)
    {
        if (outcome is not (InventoryPieceStatus.Available or InventoryPieceStatus.ReconciledUnavailable) ||
            actorUserId == Guid.Empty || operationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Trim().Length > 512 ||
            evidenceSha256 is not null && (evidenceSha256.Length != 64 ||
                !evidenceSha256.All(Uri.IsHexDigit)) ||
            observedWeightKg < 0 || countedOnHand < 0 ||
            decimal.Round(observedWeightKg, 3) != observedWeightKg ||
            decimal.Round(countedOnHand, 3) != countedOnHand ||
            countedAtUtc > createdAtUtc)
            throw new ArgumentException("Invalid historical reconciliation evidence.");
        var item = new InventoryPieceStateEvent(companyId, branchId, pieceId,
            InventoryPieceStatus.HistoricalUnverified, outcome,
            InventoryPieceEventKind.HistoricalReconciled, null, actorUserId, reason, createdAtUtc);
        item.ReconciliationOperationId = operationId;
        item.EvidenceReference = evidenceReference.Trim();
        item.EvidenceSha256 = evidenceSha256?.ToUpperInvariant();
        item.ObservedWeightKg = observedWeightKg;
        item.CountedOnHand = countedOnHand;
        item.CountedAtUtc = countedAtUtc.ToUniversalTime();
        item.InventoryMovementId = inventoryMovementId;
        return item;
    }
}
