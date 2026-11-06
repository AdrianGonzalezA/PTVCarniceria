namespace Carnicerias.Infrastructure;

public enum PhysicalStockCountStatus { Draft, Submitted, Approved, Rejected, Canceled }

/// <summary>A count snapshot does not hold a POS lock while people are counting.</summary>
public sealed class PhysicalStockCount
{
    private readonly List<PhysicalStockCountLine> _lines = [];

    private PhysicalStockCount() { }

    public PhysicalStockCount(Guid companyId, Guid branchId, Guid operationId,
        Guid createdByUserId, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty ||
            operationId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("A company, branch, operation and actor are required.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        OperationId = operationId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? SubmittedByUserId { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public PhysicalStockCountStatus Status { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<PhysicalStockCountLine> Lines => _lines.AsReadOnly();

    public void AddLine(PhysicalStockCountLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Status != PhysicalStockCountStatus.Draft ||
            line.CountId != Id || line.CompanyId != CompanyId ||
            _lines.Count >= 500 || _lines.Any(item => item.ProductId == line.ProductId))
            throw new InvalidOperationException("A product can be counted once in a draft.");
        _lines.Add(line);
        Version++;
    }

    public void Submit(Guid actorId, DateTimeOffset now)
    {
        if (Status != PhysicalStockCountStatus.Draft || actorId == Guid.Empty ||
            _lines.Count == 0)
            throw new InvalidOperationException("A nonempty draft is required.");
        Status = PhysicalStockCountStatus.Submitted;
        SubmittedByUserId = actorId;
        SubmittedAtUtc = now.ToUniversalTime();
        Version++;
    }

    public void Approve(Guid actorId, string reason, DateTimeOffset now)
    {
        if (Status != PhysicalStockCountStatus.Submitted || actorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 240)
            throw new InvalidOperationException("An administrator and reason are required.");
        Status = PhysicalStockCountStatus.Approved;
        DecidedByUserId = actorId;
        DecidedAtUtc = now.ToUniversalTime();
        DecisionReason = reason.Trim();
        Version++;
    }

    public void Reject(Guid actorId, string reason, DateTimeOffset now)
    {
        if (Status != PhysicalStockCountStatus.Submitted || actorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 240)
            throw new InvalidOperationException("An administrator and reason are required.");
        Status = PhysicalStockCountStatus.Rejected;
        DecidedByUserId = actorId;
        DecidedAtUtc = now.ToUniversalTime();
        DecisionReason = reason.Trim();
        Version++;
    }

    public void Cancel(Guid actorId, DateTimeOffset now)
    {
        if (Status != PhysicalStockCountStatus.Draft || actorId == Guid.Empty)
            throw new InvalidOperationException("Only a draft can be canceled.");
        Status = PhysicalStockCountStatus.Canceled;
        DecidedByUserId = actorId;
        DecidedAtUtc = now.ToUniversalTime();
        Version++;
    }

    public void LinkMovement(Guid lineId, Guid movementId)
    {
        if (Status != PhysicalStockCountStatus.Approved)
            throw new InvalidOperationException("Only approved counts can link stock movements.");
        _lines.Single(line => line.Id == lineId).LinkMovement(movementId);
        Version++;
    }
}

public sealed class PhysicalStockCountLine
{
    private PhysicalStockCountLine() { ProductCode = ProductName = Unit = string.Empty; }

    public PhysicalStockCountLine(Guid countId, CatalogProduct product,
        StockCountPolicy policy, decimal countedQuantity, decimal systemOnHand,
        decimal systemReserved, decimal systemProtectedOnHand,
        decimal systemProtectedReserved, decimal systemQuarantined,
        Guid? lastMovementId, Guid countedByUserId, DateTimeOffset countedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (countId == Guid.Empty || countedByUserId == Guid.Empty ||
            !product.IsActive || !Enum.IsDefined(policy) ||
            countedQuantity is < 0 or > 100_000 ||
            decimal.Round(countedQuantity, 3) != countedQuantity ||
            product.SaleMode == ProductSaleMode.Unit &&
                countedQuantity != decimal.Truncate(countedQuantity) ||
            systemReserved < 0 || systemProtectedOnHand < 0 ||
            systemProtectedReserved < 0 || systemQuarantined < 0 ||
            lastMovementId == Guid.Empty)
            throw new ArgumentException("The physical count snapshot is invalid.");
        Id = Guid.NewGuid();
        CountId = countId;
        CompanyId = product.CompanyId;
        ProductId = product.Id;
        ProductCode = product.Code;
        ProductName = product.Name;
        AnatomicalGroup = product.AnatomicalGroup;
        Unit = product.Unit;
        SaleMode = product.SaleMode;
        Policy = policy;
        CountedQuantity = countedQuantity;
        SystemOnHand = systemOnHand;
        SystemReserved = systemReserved;
        SystemProtectedOnHand = systemProtectedOnHand;
        SystemProtectedReserved = systemProtectedReserved;
        SystemQuarantined = systemQuarantined;
        LastMovementId = lastMovementId;
        CountedByUserId = countedByUserId;
        CountedAtUtc = countedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CountId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; }
    public string ProductName { get; private set; }
    public string? AnatomicalGroup { get; private set; }
    public string Unit { get; private set; }
    public ProductSaleMode SaleMode { get; private set; }
    public StockCountPolicy Policy { get; private set; }
    public decimal CountedQuantity { get; private set; }
    public decimal SystemOnHand { get; private set; }
    public decimal SystemReserved { get; private set; }
    public decimal SystemProtectedOnHand { get; private set; }
    public decimal SystemProtectedReserved { get; private set; }
    public decimal SystemQuarantined { get; private set; }
    public Guid? LastMovementId { get; private set; }
    public Guid CountedByUserId { get; private set; }
    public DateTimeOffset CountedAtUtc { get; private set; }
    public Guid? InventoryMovementId { get; private set; }
    public decimal Delta => CountedQuantity - SystemOnHand;

    public bool MatchesCurrent(BranchInventoryBalance? balance,
        Guid? lastMovementId, StockCountPolicy effectivePolicy) =>
        Policy == effectivePolicy && LastMovementId == lastMovementId &&
        (balance?.OnHand ?? 0) == SystemOnHand &&
        (balance?.Reserved ?? 0) == SystemReserved &&
        (balance?.ProtectedOnHand ?? 0) == SystemProtectedOnHand &&
        (balance?.ProtectedReserved ?? 0) == SystemProtectedReserved &&
        (balance?.Quarantined ?? 0) == SystemQuarantined;

    public bool CanApplyStockDifference() =>
        Policy != StockCountPolicy.TheoreticalStock || Delta == 0 ||
        CountedQuantity >= SystemProtectedOnHand + SystemReserved -
            SystemProtectedReserved;

    internal void LinkMovement(Guid movementId)
    {
        if (movementId == Guid.Empty || InventoryMovementId.HasValue ||
            Policy != StockCountPolicy.TheoreticalStock || Delta == 0)
            throw new InvalidOperationException("Only a stock difference can link a movement.");
        InventoryMovementId = movementId;
    }
}
