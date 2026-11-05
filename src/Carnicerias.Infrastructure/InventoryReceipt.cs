namespace Carnicerias.Infrastructure;

public enum InventoryReceiptStatus { Draft, Approved, Canceled }
public enum InventoryReceiptDifferenceStatus { None, PendingReview, Resolved }

/// <summary>A receipt records the supplier and quantities actually observed before stock changes.</summary>
public sealed class InventoryReceipt
{
    private readonly List<InventoryReceiptLine> _lines = [];

    private InventoryReceipt()
    {
        SupplierCode = string.Empty;
        SupplierName = string.Empty;
        DocumentType = string.Empty;
        DocumentNumber = string.Empty;
    }

    public InventoryReceipt(Guid companyId, Guid branchId, Guid operationId,
        Supplier supplier, string documentType, string documentNumber, DateOnly documentDate,
        Guid createdByUserId, DateTimeOffset createdAtUtc,
        string? destination = null, string? externalReference = null)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        if (!supplier.IsActive || supplier.CompanyId != companyId)
            throw new ArgumentException("An active company supplier is required.", nameof(supplier));
        ValidateIdentity(companyId, branchId, operationId, createdByUserId);
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        OperationId = operationId;
        SupplierId = supplier.Id;
        SupplierCode = supplier.Code;
        SupplierName = supplier.Name;
        SupplierIdentification = supplier.Identification;
        DocumentType = Required(documentType, 40, nameof(documentType));
        DocumentNumber = Required(documentNumber, 80, nameof(documentNumber));
        DocumentDate = documentDate == default ? throw new ArgumentException(
            "Document date is required.", nameof(documentDate)) : documentDate;
        Destination = Optional(destination, 120);
        ExternalReference = Optional(externalReference, 120);
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Status = InventoryReceiptStatus.Draft;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid? SupplierId { get; private set; }
    public string SupplierCode { get; private set; } = string.Empty;
    public string SupplierName { get; private set; } = string.Empty;
    public string? SupplierIdentification { get; private set; }
    public string? SourceSystem { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public string DocumentNumber { get; private set; } = string.Empty;
    public DateOnly DocumentDate { get; private set; }
    public string? Destination { get; private set; }
    public string? ExternalReference { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public Guid? CanceledByUserId { get; private set; }
    public DateTimeOffset? CanceledAtUtc { get; private set; }
    public InventoryReceiptStatus Status { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<InventoryReceiptLine> Lines => _lines.AsReadOnly();

    public static InventoryReceipt LegacyPiece(Guid companyId, Guid branchId, Guid operationId,
        string sourceSystem, CatalogProduct product, decimal weightKg, Guid actorUserId,
        DateTimeOffset receivedAtUtc)
    {
        ValidateIdentity(companyId, branchId, operationId, actorUserId);
        ArgumentNullException.ThrowIfNull(product);
        if (product.CompanyId != companyId || product.SaleMode != ProductSaleMode.Weight)
            throw new ArgumentException("A company weight product is required.", nameof(product));
        var receipt = new InventoryReceipt
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BranchId = branchId,
            OperationId = operationId,
            SourceSystem = Required(sourceSystem, 120, nameof(sourceSystem)),
            SupplierCode = string.Empty,
            SupplierName = string.Empty,
            DocumentType = "legacyPiece",
            DocumentNumber = operationId.ToString("N"),
            DocumentDate = DateOnly.FromDateTime(receivedAtUtc.UtcDateTime),
            CreatedByUserId = actorUserId,
            CreatedAtUtc = receivedAtUtc.ToUniversalTime(),
            Version = 1
        };
        receipt.AddLine(new InventoryReceiptLine(receipt.Id, product, weightKg));
        receipt.Approve(actorUserId, receivedAtUtc);
        return receipt;
    }

    public void AddLine(InventoryReceiptLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Status != InventoryReceiptStatus.Draft || line.ReceiptId != Id ||
            line.CompanyId != CompanyId ||
            _lines.Any(existing => existing.Id == line.Id))
            throw new InvalidOperationException("Only a new line of this draft can be added.");
        _lines.Add(line);
        Version++;
    }

    public void ReplaceLines(IEnumerable<InventoryReceiptLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var proposed = lines.ToArray();
        if (Status != InventoryReceiptStatus.Draft ||
            proposed.Any(line => line is null || line.ReceiptId != Id ||
                line.CompanyId != CompanyId || line.InventoryMovementId is not null ||
                line.DifferenceStatus != InventoryReceiptDifferenceStatus.None) ||
            proposed.Select(line => line.Id).Distinct().Count() != proposed.Length)
            throw new InvalidOperationException("Only new lines of this draft can be saved.");
        _lines.Clear();
        _lines.AddRange(proposed);
        Version++;
    }

    public void Approve(Guid actorUserId, DateTimeOffset now)
    {
        if (Status != InventoryReceiptStatus.Draft || actorUserId == Guid.Empty ||
            _lines.Count == 0 || _lines.All(line => line.ReceivedQuantity == 0))
            throw new InvalidOperationException("A receipt with actual quantities is required.");
        Status = InventoryReceiptStatus.Approved;
        ApprovedByUserId = actorUserId;
        ApprovedAtUtc = now.ToUniversalTime();
        Version++;
        foreach (var line in _lines) line.MarkApproved();
    }

    public void Cancel(Guid actorUserId, DateTimeOffset now)
    {
        if (Status != InventoryReceiptStatus.Draft || actorUserId == Guid.Empty)
            throw new InvalidOperationException("Only a draft can be canceled.");
        Status = InventoryReceiptStatus.Canceled;
        CanceledByUserId = actorUserId;
        CanceledAtUtc = now.ToUniversalTime();
        Version++;
    }

    public void ResolveDifference(Guid lineId, Guid administratorId,
        string reason, DateTimeOffset now)
    {
        if (Status != InventoryReceiptStatus.Approved || administratorId == Guid.Empty)
            throw new InvalidOperationException("An approved receipt and administrator are required.");
        var line = _lines.SingleOrDefault(item => item.Id == lineId)
            ?? throw new ArgumentException("Receipt line not found.", nameof(lineId));
        line.ResolveDifference(administratorId, reason, now);
        Version++;
    }

    public void LinkMovement(Guid lineId, Guid movementId)
    {
        if (Status != InventoryReceiptStatus.Approved)
            throw new InvalidOperationException("Only an approved receipt can link a stock movement.");
        var line = _lines.SingleOrDefault(item => item.Id == lineId)
            ?? throw new ArgumentException("Receipt line not found.", nameof(lineId));
        line.LinkMovement(movementId);
        Version++;
    }

    private static void ValidateIdentity(Guid companyId, Guid branchId, Guid operationId, Guid userId)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty ||
            operationId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Receipt identifiers are required.");
    }

    private static string Required(string? value, int maximum, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximum)
            throw new ArgumentException("Receipt field is required and must fit its limit.", name);
        return normalized;
    }

    private static string? Optional(string? value, int maximum)
    {
        var normalized = value?.Trim();
        if (normalized?.Length > maximum) throw new ArgumentException("Receipt field exceeds its limit.");
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public sealed class InventoryReceiptLine
{
    private InventoryReceiptLine()
    {
        ProductCode = string.Empty;
        ProductName = string.Empty;
        Unit = string.Empty;
    }

    public InventoryReceiptLine(Guid receiptId, CatalogProduct product,
        decimal receivedQuantity, decimal? expectedQuantity = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (receiptId == Guid.Empty || !product.IsActive ||
            receivedQuantity < 0 || receivedQuantity > 100_000 ||
            decimal.Round(receivedQuantity, 3) != receivedQuantity ||
            expectedQuantity is < 0 or > 100_000 ||
            expectedQuantity.HasValue && decimal.Round(expectedQuantity.Value, 3) != expectedQuantity ||
            receivedQuantity == 0 && expectedQuantity is null or 0 ||
            product.SaleMode == ProductSaleMode.Unit &&
                (receivedQuantity != decimal.Truncate(receivedQuantity) ||
                 expectedQuantity.HasValue && expectedQuantity != decimal.Truncate(expectedQuantity.Value)))
            throw new ArgumentException("Receipt quantities must match the product unit.");
        Id = Guid.NewGuid();
        ReceiptId = receiptId;
        CompanyId = product.CompanyId;
        ProductId = product.Id;
        ProductCode = product.Code;
        ProductName = product.Name;
        Unit = product.Unit;
        SaleMode = product.SaleMode;
        ReceivedQuantity = receivedQuantity;
        ExpectedQuantity = expectedQuantity;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public ProductSaleMode SaleMode { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal? ExpectedQuantity { get; private set; }
    public InventoryReceiptDifferenceStatus DifferenceStatus { get; private set; }
    public Guid? DifferenceResolvedByUserId { get; private set; }
    public DateTimeOffset? DifferenceResolvedAtUtc { get; private set; }
    public string? DifferenceReason { get; private set; }
    public Guid? InventoryMovementId { get; private set; }

    internal void LinkMovement(Guid movementId)
    {
        if (movementId == Guid.Empty || ReceivedQuantity == 0 || InventoryMovementId is not null)
            throw new InvalidOperationException("A received line can have one stock movement.");
        InventoryMovementId = movementId;
    }

    internal void MarkApproved()
    {
        DifferenceStatus = ExpectedQuantity.HasValue &&
            ExpectedQuantity.Value != ReceivedQuantity
            ? InventoryReceiptDifferenceStatus.PendingReview
            : InventoryReceiptDifferenceStatus.None;
    }

    internal void ResolveDifference(Guid administratorId, string reason, DateTimeOffset now)
    {
        if (DifferenceStatus != InventoryReceiptDifferenceStatus.PendingReview ||
            administratorId == Guid.Empty || string.IsNullOrWhiteSpace(reason) ||
            reason.Trim().Length > 240)
            throw new InvalidOperationException("A pending difference and reason are required.");
        DifferenceStatus = InventoryReceiptDifferenceStatus.Resolved;
        DifferenceResolvedByUserId = administratorId;
        DifferenceResolvedAtUtc = now.ToUniversalTime();
        DifferenceReason = reason.Trim();
    }
}
