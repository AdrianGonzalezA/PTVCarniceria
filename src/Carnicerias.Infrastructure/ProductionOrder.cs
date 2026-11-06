namespace Carnicerias.Infrastructure;

public enum ProductionOrderKind { Standard, Correction }
public enum ProductionOrderStatus { Open, Closed, Canceled }
public enum ProductionLineDirection { Input, Output }

public sealed class ProductionOrder
{
    private readonly List<ProductionMaterialLine> _lines = [];
    private ProductionOrder() => WasteReason = string.Empty;

    public ProductionOrder(Guid companyId, Guid branchId, Guid operationId,
        Guid createdByUserId, DateTimeOffset createdAtUtc, Guid? classifierId,
        Guid? correctsOrderId = null)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || operationId == Guid.Empty ||
            createdByUserId == Guid.Empty || classifierId == Guid.Empty ||
            correctsOrderId == Guid.Empty ||
            classifierId is null == (correctsOrderId is null))
            throw new ArgumentException("Choose one production classifier or an order to correct.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        OperationId = operationId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ClassifierId = classifierId;
        CorrectsOrderId = correctsOrderId;
        Kind = correctsOrderId is null ? ProductionOrderKind.Standard : ProductionOrderKind.Correction;
        Status = ProductionOrderStatus.Open;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? ClassifierId { get; private set; }
    public Guid? CorrectsOrderId { get; private set; }
    public ProductionOrderKind Kind { get; private set; }
    public ProductionOrderStatus Status { get; private set; }
    public int Version { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public Guid? CanceledByUserId { get; private set; }
    public DateTimeOffset? CanceledAtUtc { get; private set; }
    public decimal ReportedWasteKg { get; private set; }
    public string WasteReason { get; private set; } = string.Empty;
    public IReadOnlyList<ProductionMaterialLine> Lines => _lines.AsReadOnly();
    public decimal InputMassKg => _lines.Where(line => line.Direction == ProductionLineDirection.Input)
        .Sum(line => line.MassKg);
    public decimal OutputMassKg => _lines.Where(line => line.Direction == ProductionLineDirection.Output)
        .Sum(line => line.MassKg);
    public decimal MassDifferenceKg => InputMassKg - OutputMassKg - ReportedWasteKg;

    public void ReplaceLines(IEnumerable<ProductionMaterialLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (Status != ProductionOrderStatus.Open)
            throw new InvalidOperationException("Only an open production order can be edited.");
        var proposed = lines.ToArray();
        if (proposed.Any(line => line.CompanyId != CompanyId || line.OrderId != Id ||
                line.InventoryMovementId is not null || line.OutputPieceId is not null) ||
            proposed.Select(line => line.Id).Distinct().Count() != proposed.Length ||
            proposed.Where(line => line.InputPieceId.HasValue).Select(line => line.InputPieceId)
                .Distinct().Count() != proposed.Count(line => line.InputPieceId.HasValue) ||
            proposed.Where(line => line.OutputExternalIdentifier is not null)
                .Select(line => (line.OutputSourceSystem, line.OutputExternalIdentifier))
                .Distinct().Count() != proposed.Count(line => line.OutputExternalIdentifier is not null))
            throw new ArgumentException("Production lines are duplicated or belong to another order.",
                nameof(lines));
        _lines.Clear();
        _lines.AddRange(proposed);
        Version++;
    }

    public void Close(Guid actorUserId, decimal reportedWasteKg, string? wasteReason,
        DateTimeOffset now)
    {
        if (Status != ProductionOrderStatus.Open || actorUserId == Guid.Empty ||
            !_lines.Any(line => line.Direction == ProductionLineDirection.Input) ||
            !_lines.Any(line => line.Direction == ProductionLineDirection.Output))
            throw new InvalidOperationException("A nonempty open order and actor are required.");
        if (reportedWasteKg < 0 || reportedWasteKg > 100_000 ||
            decimal.Round(reportedWasteKg, 3) != reportedWasteKg ||
            reportedWasteKg > 0 && (string.IsNullOrWhiteSpace(wasteReason) ||
                wasteReason.Trim().Length > 240) ||
            wasteReason?.Trim().Length > 240)
            throw new ArgumentException("Waste quantity or reason is invalid.");
        ReportedWasteKg = reportedWasteKg;
        WasteReason = wasteReason?.Trim() ?? string.Empty;
        ClosedByUserId = actorUserId;
        ClosedAtUtc = now.ToUniversalTime();
        Status = ProductionOrderStatus.Closed;
        Version++;
    }

    public void Cancel(Guid actorUserId, DateTimeOffset now)
    {
        if (Status != ProductionOrderStatus.Open || actorUserId == Guid.Empty)
            throw new InvalidOperationException("Only an open order can be canceled.");
        CanceledByUserId = actorUserId;
        CanceledAtUtc = now.ToUniversalTime();
        Status = ProductionOrderStatus.Canceled;
        Version++;
    }

    public void LinkMovement(Guid lineId, Guid movementId, Guid? outputPieceId = null)
    {
        if (Status != ProductionOrderStatus.Closed)
            throw new InvalidOperationException("Only a closed order can link movements.");
        var line = _lines.SingleOrDefault(item => item.Id == lineId)
            ?? throw new ArgumentException("Production line not found.", nameof(lineId));
        line.LinkMovement(movementId, outputPieceId);
    }
}

public sealed class ProductionMaterialLine
{
    private ProductionMaterialLine() => ProductCode = ProductName = Unit = string.Empty;

    public ProductionMaterialLine(Guid orderId, CatalogProduct product,
        ProductionLineDirection direction, decimal quantity, decimal massKg,
        Guid? inputPieceId = null, Guid? outputBarcodeProfileId = null,
        string? outputSourceSystem = null, string? outputExternalIdentifier = null,
        string? outputRawBarcode = null, string? outputIdentifierField = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (orderId == Guid.Empty || !product.IsActive || !Enum.IsDefined(direction) ||
            quantity <= 0 || quantity > 100_000 || massKg <= 0 || massKg > 100_000 ||
            decimal.Round(quantity, 3) != quantity || decimal.Round(massKg, 3) != massKg ||
            product.SaleMode == ProductSaleMode.Weight && quantity != massKg ||
            product.SaleMode == ProductSaleMode.Unit && quantity != decimal.Truncate(quantity) ||
            inputPieceId == Guid.Empty || outputBarcodeProfileId == Guid.Empty ||
            inputPieceId.HasValue && (direction != ProductionLineDirection.Input ||
                product.SaleMode != ProductSaleMode.Weight) ||
            outputBarcodeProfileId.HasValue && (direction != ProductionLineDirection.Output ||
                product.SaleMode != ProductSaleMode.Weight) ||
            direction == ProductionLineDirection.Input &&
                (outputSourceSystem is not null || outputExternalIdentifier is not null ||
                    outputRawBarcode is not null || outputIdentifierField is not null) ||
            direction == ProductionLineDirection.Output && inputPieceId.HasValue ||
            outputBarcodeProfileId.HasValue != (outputExternalIdentifier is not null) ||
            !outputBarcodeProfileId.HasValue && (outputSourceSystem is not null ||
                outputRawBarcode is not null || outputIdentifierField is not null) ||
            outputBarcodeProfileId.HasValue &&
                (string.IsNullOrWhiteSpace(outputSourceSystem) || outputSourceSystem.Length > 120 ||
                    string.IsNullOrWhiteSpace(outputExternalIdentifier) ||
                    outputExternalIdentifier.Length > 80 ||
                    string.IsNullOrWhiteSpace(outputRawBarcode) || outputRawBarcode.Length > 80 ||
                    string.IsNullOrWhiteSpace(outputIdentifierField) || outputIdentifierField.Length > 80))
            throw new ArgumentException("Production line quantity, mass or piece data is invalid.");

        Id = Guid.NewGuid();
        OrderId = orderId;
        CompanyId = product.CompanyId;
        ProductId = product.Id;
        ProductCode = product.Code;
        ProductName = product.Name;
        Unit = product.Unit;
        SaleMode = product.SaleMode;
        Direction = direction;
        Quantity = quantity;
        MassKg = massKg;
        InputPieceId = inputPieceId;
        OutputBarcodeProfileId = outputBarcodeProfileId;
        OutputSourceSystem = outputSourceSystem?.Trim();
        OutputExternalIdentifier = outputExternalIdentifier?.Trim();
        OutputRawBarcode = outputRawBarcode?.Trim();
        OutputIdentifierField = outputIdentifierField?.Trim();
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public ProductSaleMode SaleMode { get; private set; }
    public ProductionLineDirection Direction { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal MassKg { get; private set; }
    public Guid? InputPieceId { get; private set; }
    public Guid? OutputBarcodeProfileId { get; private set; }
    public string? OutputSourceSystem { get; private set; }
    public string? OutputExternalIdentifier { get; private set; }
    public string? OutputRawBarcode { get; private set; }
    public string? OutputIdentifierField { get; private set; }
    public Guid? InventoryMovementId { get; private set; }
    public Guid? OutputPieceId { get; private set; }

    internal void LinkMovement(Guid movementId, Guid? outputPieceId)
    {
        if (movementId == Guid.Empty || InventoryMovementId is not null ||
            outputPieceId == Guid.Empty || outputPieceId.HasValue != OutputBarcodeProfileId.HasValue)
            throw new InvalidOperationException("A production line can link one valid stock movement.");
        InventoryMovementId = movementId;
        OutputPieceId = outputPieceId;
    }
}
