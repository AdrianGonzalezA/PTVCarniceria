using System.ComponentModel.DataAnnotations.Schema;

namespace Carnicerias.Infrastructure;

public enum InventoryMovementKind
{
    OpeningBalance,
    Adjustment,
    Sale,
    PieceReceipt,
    Receipt
}

public sealed class BranchInventoryBalance
{
    private BranchInventoryBalance() { }

    public BranchInventoryBalance(Guid companyId, Guid branchId, Guid productId)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ProductId = productId;
    }

    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal OnHand { get; private set; }
    public decimal Reserved { get; private set; }
    public decimal Quarantined { get; private set; }
    public decimal ProtectedOnHand { get; private set; }
    public decimal ProtectedReserved { get; private set; }
    [NotMapped] public decimal Available => OnHand - Reserved - Quarantined;
    [NotMapped] public decimal GenericOnHand => OnHand - ProtectedOnHand;
    [NotMapped] public decimal GenericReserved => Reserved - ProtectedReserved;
    [NotMapped] public decimal GenericAvailable => GenericOnHand - GenericReserved;
    [NotMapped] public decimal ProtectedAvailable =>
        ProtectedOnHand - ProtectedReserved - Quarantined;

    public Guid? ReconciliationLockId { get; private set; }
    public Guid? ReconciliationLockPieceId { get; private set; }
    public Guid? ReconciliationLockOwnerUserId { get; private set; }
    public DateTimeOffset? ReconciliationLockStartedAtUtc { get; private set; }
    public DateTimeOffset? ReconciliationLockExpiresAtUtc { get; private set; }
    public bool IsReconciliationLocked(DateTimeOffset now) =>
        ReconciliationLockId is not null && ReconciliationLockExpiresAtUtc > now;

    public void SetQuantities(decimal onHand, decimal reserved, decimal? quarantined = null,
        decimal? protectedOnHand = null, decimal? protectedReserved = null,
        bool allowGenericDeficit = false)
    {
        var held = quarantined ?? Quarantined;
        var protectedTotal = protectedOnHand ?? Math.Max(ProtectedOnHand, held);
        var protectedHeld = protectedReserved ?? ProtectedReserved;
        if (reserved < 0 || held < 0 || protectedTotal < 0 || protectedHeld < 0 ||
            reserved < protectedHeld || protectedTotal < protectedHeld + held ||
            !allowGenericDeficit && (onHand < 0 || reserved + held > onHand))
            throw new ArgumentOutOfRangeException(nameof(onHand));
        OnHand = onHand;
        Reserved = reserved;
        Quarantined = held;
        ProtectedOnHand = protectedTotal;
        ProtectedReserved = protectedHeld;
    }
}

public sealed class InventoryMovement
{
    private InventoryMovement() => Reason = string.Empty;

    public static string NormalizeAdjustmentDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? "Ajuste manual" : description.Trim();

    public InventoryMovement(Guid companyId, Guid branchId, Guid productId, Guid userId,
        Guid operationId, InventoryMovementKind kind, decimal quantityDelta, string reason, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty || userId == Guid.Empty || operationId == Guid.Empty)
            throw new ArgumentException("Inventory movement identifiers are required.");
        if (quantityDelta == 0 || decimal.Round(quantityDelta, 3) != quantityDelta)
            throw new ArgumentOutOfRangeException(nameof(quantityDelta));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 240)
            throw new ArgumentException("A reason of at most 240 characters is required.", nameof(reason));
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        ProductId = productId;
        UserId = userId;
        OperationId = operationId;
        Kind = kind;
        QuantityDelta = quantityDelta;
        Reason = reason.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid OperationId { get; private set; }
    public InventoryMovementKind Kind { get; private set; }
    public decimal QuantityDelta { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
