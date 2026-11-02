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
    Consumed
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
}
