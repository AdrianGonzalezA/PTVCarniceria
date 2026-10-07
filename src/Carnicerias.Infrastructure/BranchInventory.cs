namespace Carnicerias.Infrastructure;

public enum InventoryMovementKind
{
    OpeningBalance,
    Adjustment,
    Sale
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

    public void SetQuantities(decimal onHand, decimal reserved)
    {
        OnHand = onHand;
        Reserved = reserved;
    }
}

public sealed class InventoryMovement
{
    private InventoryMovement() => Reason = string.Empty;

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
