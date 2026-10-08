namespace Carnicerias.Infrastructure;

public enum SaleDraftStatus
{
    Draft,
    Cancelled,
    Confirmed
}

public sealed class SaleDraft
{
    private SaleDraft() { }

    public SaleDraft(Guid companyId, Guid branchId, Guid userId, Guid priceListId,
        DateTimeOffset createdAtUtc, Guid? posTerminalId = null, Guid? cashierShiftId = null)
    {
        if (posTerminalId == Guid.Empty || cashierShiftId == Guid.Empty ||
            (posTerminalId is null) != (cashierShiftId is null))
            throw new ArgumentException("Terminal and shift ids must be provided together.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        UserId = userId;
        PosTerminalId = posTerminalId;
        CashierShiftId = cashierShiftId;
        PriceListId = priceListId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? PosTerminalId { get; private set; }
    public Guid? CashierShiftId { get; private set; }
    public Guid PriceListId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public SaleDraftStatus Status { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public Guid? ConfirmedSaleId { get; private set; }
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }
    public List<SaleDraftLine> Lines { get; private set; } = [];

    public void ReplaceLines(IEnumerable<SaleDraftLine> lines, DateTimeOffset updatedAtUtc)
    {
        var requestedLines = lines.ToDictionary(line => (line.ProductId, line.InventoryPieceId));
        foreach (var existingLine in Lines.ToArray())
        {
            if (requestedLines.Remove((existingLine.ProductId, existingLine.InventoryPieceId), out var requestedLine))
                existingLine.UpdateQuantity(requestedLine.Quantity);
            else
                Lines.Remove(existingLine);
        }
        Lines.AddRange(requestedLines.Values);
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public void Cancel(Guid userId, DateTimeOffset cancelledAtUtc)
    {
        if (Status != SaleDraftStatus.Draft || userId == Guid.Empty)
            throw new InvalidOperationException("Only an active draft can be cancelled.");
        Status = SaleDraftStatus.Cancelled;
        CancelledAtUtc = cancelledAtUtc.ToUniversalTime();
        CancelledByUserId = userId;
        UpdatedAtUtc = CancelledAtUtc.Value;
    }

    public void Confirm(Guid saleId, DateTimeOffset confirmedAtUtc)
    {
        if (Status != SaleDraftStatus.Draft || saleId == Guid.Empty)
            throw new InvalidOperationException("Only an active draft can be confirmed.");
        Status = SaleDraftStatus.Confirmed;
        ConfirmedSaleId = saleId;
        ConfirmedAtUtc = confirmedAtUtc.ToUniversalTime();
        UpdatedAtUtc = ConfirmedAtUtc.Value;
    }
}

public sealed class SaleDraftLine
{
    private SaleDraftLine()
    {
        ProductCode = string.Empty;
        ProductName = string.Empty;
        Unit = string.Empty;
    }

    public SaleDraftLine(Guid companyId, Guid productId, string productCode, string productName,
        string unit, ProductSaleMode saleMode, decimal quantity, decimal unitPrice,
        Guid? inventoryPieceId = null, string? pieceIdentifier = null)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        ProductId = productId;
        ProductCode = productCode;
        ProductName = productName;
        Unit = unit;
        SaleMode = saleMode;
        Quantity = quantity;
        UnitPrice = unitPrice;
        InventoryPieceId = inventoryPieceId;
        PieceIdentifier = pieceIdentifier;
    }

    public Guid Id { get; private set; }
    public Guid SaleDraftId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; }
    public string ProductName { get; private set; }
    public string Unit { get; private set; }
    public ProductSaleMode SaleMode { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public Guid? InventoryPieceId { get; private set; }
    public string? PieceIdentifier { get; private set; }

    public void UpdateQuantity(decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        Quantity = quantity;
    }
}
