namespace Carnicerias.Infrastructure;

public enum InventoryTransferStatus { Draft, Dispatched, Canceled }

/// <summary>A dispatch is final at the origin even when the destination has no POS.</summary>
public sealed class InventoryTransfer
{
    private readonly List<InventoryTransferLine> _lines = [];

    private InventoryTransfer() => DestinationName = DocumentNumber = string.Empty;

    public InventoryTransfer(Guid companyId, Guid branchId, Guid operationId,
        string destinationName, Guid? destinationBranchId, Guid createdByUserId,
        DateTimeOffset createdAtUtc, DateOnly? documentDate = null)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || operationId == Guid.Empty ||
            createdByUserId == Guid.Empty || destinationBranchId == Guid.Empty ||
            destinationBranchId == branchId || string.IsNullOrWhiteSpace(destinationName) ||
            destinationName.Trim().Length > 200 ||
            documentDate.HasValue && documentDate.Value == default)
            throw new ArgumentException("A valid transfer origin, destination and actor are required.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        OperationId = operationId;
        DestinationBranchId = destinationBranchId;
        DestinationName = destinationName.Trim();
        DocumentNumber = $"TR-{operationId:N}";
        DocumentDate = documentDate ?? DateOnly.FromDateTime(createdAtUtc.UtcDateTime);
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Status = InventoryTransferStatus.Draft;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid? DestinationBranchId { get; private set; }
    public string DestinationName { get; private set; } = string.Empty;
    public string DocumentNumber { get; private set; } = string.Empty;
    public DateOnly DocumentDate { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? DispatchedByUserId { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }
    public Guid? CanceledByUserId { get; private set; }
    public DateTimeOffset? CanceledAtUtc { get; private set; }
    public InventoryTransferStatus Status { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<InventoryTransferLine> Lines => _lines.AsReadOnly();

    public void ReplaceLines(IEnumerable<InventoryTransferLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var proposed = lines.ToArray();
        if (Status != InventoryTransferStatus.Draft)
            throw new InvalidOperationException("Only a draft transfer can be edited.");
        if (proposed.Any(line => line.TransferId != Id || line.CompanyId != CompanyId ||
                line.InventoryMovementId is not null) ||
            proposed.Select(line => line.Id).Distinct().Count() != proposed.Length ||
            proposed.Where(line => line.PieceId.HasValue).Select(line => line.PieceId)
                .Distinct().Count() != proposed.Count(line => line.PieceId.HasValue))
            throw new ArgumentException("Transfer lines are duplicated or belong to another document.",
                nameof(lines));
        _lines.Clear();
        _lines.AddRange(proposed);
        Version++;
    }

    public void Dispatch(Guid actorUserId, DateTimeOffset now)
    {
        if (Status != InventoryTransferStatus.Draft || actorUserId == Guid.Empty ||
            _lines.Count == 0)
            throw new InvalidOperationException("A nonempty draft and actor are required to dispatch.");
        Status = InventoryTransferStatus.Dispatched;
        DispatchedByUserId = actorUserId;
        DispatchedAtUtc = now.ToUniversalTime();
        Version++;
    }

    public void Cancel(Guid actorUserId, DateTimeOffset now)
    {
        if (Status != InventoryTransferStatus.Draft || actorUserId == Guid.Empty)
            throw new InvalidOperationException("Only a draft transfer can be canceled.");
        Status = InventoryTransferStatus.Canceled;
        CanceledByUserId = actorUserId;
        CanceledAtUtc = now.ToUniversalTime();
        Version++;
    }

    public void LinkMovement(Guid lineId, Guid movementId)
    {
        if (Status != InventoryTransferStatus.Dispatched)
            throw new InvalidOperationException("Only a dispatched transfer can link stock movements.");
        var line = _lines.SingleOrDefault(item => item.Id == lineId)
            ?? throw new ArgumentException("Transfer line not found.", nameof(lineId));
        line.LinkMovement(movementId);
        Version++;
    }
}

public sealed class InventoryTransferLine
{
    private InventoryTransferLine() => ProductCode = ProductName = Unit = string.Empty;

    public InventoryTransferLine(Guid transferId, CatalogProduct product, decimal quantity,
        Guid? pieceId = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (transferId == Guid.Empty || !product.IsActive || quantity <= 0 ||
            quantity > 100_000 || decimal.Round(quantity, 3) != quantity ||
            pieceId == Guid.Empty || pieceId.HasValue && product.SaleMode != ProductSaleMode.Weight ||
            product.SaleMode == ProductSaleMode.Unit && quantity != decimal.Truncate(quantity))
            throw new ArgumentException("Transfer quantity or piece does not match the product.");
        Id = Guid.NewGuid();
        TransferId = transferId;
        CompanyId = product.CompanyId;
        ProductId = product.Id;
        ProductCode = product.Code;
        ProductName = product.Name;
        Unit = product.Unit;
        SaleMode = product.SaleMode;
        Quantity = quantity;
        PieceId = pieceId;
    }

    public Guid Id { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public ProductSaleMode SaleMode { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid? PieceId { get; private set; }
    public Guid? InventoryMovementId { get; private set; }

    internal void LinkMovement(Guid movementId)
    {
        if (movementId == Guid.Empty || InventoryMovementId is not null)
            throw new InvalidOperationException("A transfer line can link one stock movement.");
        InventoryMovementId = movementId;
    }
}
