using System.Text;

namespace Carnicerias.Infrastructure;

public enum InventoryTransferReceiptStatus { Draft, Approved, Canceled }
public enum InventoryTransferDifferenceStatus { None, PendingReview, Resolved }

/// <summary>Physical receipt at a destination branch; the origin dispatch is independent.</summary>
public sealed class InventoryTransferReceipt
{
    private readonly List<InventoryTransferReceiptLine> _lines = [];

    private InventoryTransferReceipt()
    {
        SourceCompanyCode = SourceBranchName = SourceBranchKey = DocumentNumber =
            DocumentKey = string.Empty;
    }

    public InventoryTransferReceipt(Guid companyId, Guid branchId, Guid operationId,
        string sourceCompanyCode, string sourceBranchName, string documentNumber,
        DateOnly documentDate, Guid createdByUserId, DateTimeOffset createdAtUtc,
        Guid? sourceBranchId = null, Guid? sourceTransferId = null,
        string? importedSha256 = null)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || operationId == Guid.Empty ||
            createdByUserId == Guid.Empty || documentDate == default ||
            sourceBranchId == Guid.Empty || sourceTransferId == Guid.Empty ||
            sourceBranchId == branchId)
            throw new ArgumentException("A destination, origin, remito and actor are required.");
        SourceCompanyCode = Required(sourceCompanyCode, 80, nameof(sourceCompanyCode));
        SourceBranchName = Required(sourceBranchName, 200, nameof(sourceBranchName));
        SourceBranchKey = Normalize(SourceBranchName);
        DocumentNumber = Required(documentNumber, 80, nameof(documentNumber));
        DocumentKey = Normalize(DocumentNumber);
        if (importedSha256 is not null && (importedSha256.Length != 64 ||
            importedSha256.Any(character => !Uri.IsHexDigit(character))))
            throw new ArgumentException("A SHA-256 hex digest is required.", nameof(importedSha256));
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        OperationId = operationId;
        SourceBranchId = sourceBranchId;
        SourceTransferId = sourceTransferId;
        DocumentDate = documentDate;
        ImportedSha256 = importedSha256?.ToLowerInvariant();
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OperationId { get; private set; }
    public string SourceCompanyCode { get; private set; }
    public string SourceBranchName { get; private set; }
    public string SourceBranchKey { get; private set; }
    public Guid? SourceBranchId { get; private set; }
    public Guid? SourceTransferId { get; private set; }
    public string DocumentNumber { get; private set; }
    public string DocumentKey { get; private set; }
    public DateOnly DocumentDate { get; private set; }
    public string? ImportedSha256 { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public Guid? CanceledByUserId { get; private set; }
    public DateTimeOffset? CanceledAtUtc { get; private set; }
    public InventoryTransferReceiptStatus Status { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<InventoryTransferReceiptLine> Lines => _lines.AsReadOnly();

    public void ReplaceLines(IEnumerable<InventoryTransferReceiptLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var proposed = lines.ToArray();
        if (Status != InventoryTransferReceiptStatus.Draft || proposed.Length > 500 ||
            proposed.Any(line => line.ReceiptId != Id || line.CompanyId != CompanyId ||
                line.InventoryMovementId is not null || line.InventoryPieceId is not null) ||
            proposed.Select(line => line.Id).Distinct().Count() != proposed.Length ||
            proposed.Where(line => line.SourceLineId.HasValue)
                .Select(line => line.SourceLineId).Distinct().Count() !=
            proposed.Count(line => line.SourceLineId.HasValue) ||
            proposed.Where(line => line.SourcePieceId.HasValue)
                .Select(line => line.SourcePieceId).Distinct().Count() !=
            proposed.Count(line => line.SourcePieceId.HasValue))
            throw new InvalidOperationException("Only distinct draft lines of this remito can be saved.");
        _lines.Clear();
        _lines.AddRange(proposed);
        Version++;
    }

    public void AddLine(InventoryTransferReceiptLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        ReplaceLines([.._lines, line]);
    }

    public void RemoveLine(Guid lineId)
    {
        if (_lines.All(line => line.Id != lineId))
            throw new ArgumentException("Line not found.", nameof(lineId));
        ReplaceLines(_lines.Where(line => line.Id != lineId).ToArray());
    }

    public void SetGenericReceived(Guid lineId, decimal quantity)
    {
        if (Status != InventoryTransferReceiptStatus.Draft)
            throw new InvalidOperationException("Only a draft can be edited.");
        _lines.Single(line => line.Id == lineId).SetGenericReceived(quantity);
        Version++;
    }

    public void CapturePiece(Guid lineId, Guid barcodeProfileId, string sourceSystem,
        string identifierField, string externalIdentifier, string rawBarcode,
        decimal weightKg)
    {
        if (Status != InventoryTransferReceiptStatus.Draft)
            throw new InvalidOperationException("Only a draft can be scanned.");
        _lines.Single(line => line.Id == lineId).CapturePiece(barcodeProfileId,
            sourceSystem, identifierField, externalIdentifier, rawBarcode, weightKg);
        Version++;
    }

    public void Approve(Guid actorUserId, DateTimeOffset now)
    {
        if (Status != InventoryTransferReceiptStatus.Draft || actorUserId == Guid.Empty ||
            _lines.Count == 0 ||
            _lines.Any(line => line.SentQuantity == 0 && line.ReceivedQuantity == 0) ||
            _lines.Any(line => line.IsPiece && line.ReceivedQuantity > 0 && !line.HasPhysicalRead))
            throw new InvalidOperationException("Physical received quantities and piece reads are required.");
        Status = InventoryTransferReceiptStatus.Approved;
        ApprovedByUserId = actorUserId;
        ApprovedAtUtc = now.ToUniversalTime();
        Version++;
        foreach (var line in _lines) line.MarkApproved();
    }

    public void Cancel(Guid actorUserId, DateTimeOffset now)
    {
        if (Status != InventoryTransferReceiptStatus.Draft || actorUserId == Guid.Empty)
            throw new InvalidOperationException("Only a draft can be canceled.");
        Status = InventoryTransferReceiptStatus.Canceled;
        CanceledByUserId = actorUserId;
        CanceledAtUtc = now.ToUniversalTime();
        Version++;
    }

    public void LinkMovement(Guid lineId, Guid movementId, Guid? pieceId = null)
    {
        if (Status != InventoryTransferReceiptStatus.Approved)
            throw new InvalidOperationException("Only approved remitos can link stock movements.");
        var line = _lines.Single(item => item.Id == lineId);
        line.LinkMovement(movementId, pieceId);
        Version++;
    }

    public void ResolveDifference(Guid lineId, Guid actorUserId, string reason,
        DateTimeOffset now)
    {
        if (Status != InventoryTransferReceiptStatus.Approved || actorUserId == Guid.Empty)
            throw new InvalidOperationException("Only an approved remito can resolve a difference.");
        _lines.Single(item => item.Id == lineId).ResolveDifference(actorUserId, reason, now);
        Version++;
    }

    private static string Required(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new ArgumentException("A nonempty value within the length limit is required.", name);
        return value.Trim().Normalize(NormalizationForm.FormKC);
    }

    private static string Normalize(string value) => value.ToUpperInvariant();
}

public sealed class InventoryTransferReceiptLine
{
    private InventoryTransferReceiptLine()
    {
        ProductCode = ProductName = Unit = string.Empty;
    }

    public InventoryTransferReceiptLine(Guid receiptId, CatalogProduct product,
        decimal sentQuantity, decimal receivedQuantity, bool isPiece = false,
        Guid? sourceLineId = null, Guid? sourcePieceId = null,
        string? sourceSystem = null, string? sourceExternalIdentifier = null,
        string? sourceRawBarcode = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (receiptId == Guid.Empty || !product.IsActive ||
            !ValidQuantity(sentQuantity, product.SaleMode) ||
            !ValidQuantity(receivedQuantity, product.SaleMode) ||
            sentQuantity == 0 && receivedQuantity == 0 && !isPiece ||
            sourceLineId == Guid.Empty || sourcePieceId == Guid.Empty ||
            isPiece && product.SaleMode != ProductSaleMode.Weight ||
            isPiece && receivedQuantity != 0 ||
            !isPiece && (sourcePieceId.HasValue || sourceSystem is not null ||
                sourceExternalIdentifier is not null || sourceRawBarcode is not null))
            throw new ArgumentException("Transfer receipt quantities or piece identity are invalid.");
        Id = Guid.NewGuid();
        ReceiptId = receiptId;
        CompanyId = product.CompanyId;
        ProductId = product.Id;
        ProductCode = product.Code;
        ProductName = product.Name;
        Unit = product.Unit;
        SaleMode = product.SaleMode;
        SentQuantity = sentQuantity;
        ReceivedQuantity = receivedQuantity;
        IsPiece = isPiece;
        SourceLineId = sourceLineId;
        SourcePieceId = sourcePieceId;
        SourceSystem = Optional(sourceSystem, 120);
        SourceExternalIdentifier = Optional(sourceExternalIdentifier, 80);
        SourceRawBarcode = Optional(sourceRawBarcode, 80);
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; }
    public string ProductName { get; private set; }
    public string Unit { get; private set; }
    public ProductSaleMode SaleMode { get; private set; }
    public decimal SentQuantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public bool IsPiece { get; private set; }
    public Guid? SourceLineId { get; private set; }
    public Guid? SourcePieceId { get; private set; }
    public string? SourceSystem { get; private set; }
    public string? SourceExternalIdentifier { get; private set; }
    public string? SourceRawBarcode { get; private set; }
    public Guid? BarcodeProfileId { get; private set; }
    public string? ReadSourceSystem { get; private set; }
    public string? IdentifierField { get; private set; }
    public string? ExternalIdentifier { get; private set; }
    public string? RawBarcode { get; private set; }
    public bool SourcePieceVerified { get; private set; }
    public Guid? InventoryMovementId { get; private set; }
    public Guid? InventoryPieceId { get; private set; }
    public InventoryTransferDifferenceStatus DifferenceStatus { get; private set; }
    public string? DifferenceReason { get; private set; }
    public Guid? DifferenceResolvedByUserId { get; private set; }
    public DateTimeOffset? DifferenceResolvedAtUtc { get; private set; }
    public bool HasPhysicalRead => BarcodeProfileId.HasValue &&
        !string.IsNullOrWhiteSpace(ExternalIdentifier);

    public void SetGenericReceived(decimal quantity)
    {
        if (IsPiece || !ValidQuantity(quantity, SaleMode) ||
            InventoryMovementId is not null)
            throw new InvalidOperationException("Only a generic draft line can change quantity.");
        ReceivedQuantity = quantity;
    }

    public void CapturePiece(Guid barcodeProfileId, string sourceSystem,
        string identifierField, string externalIdentifier, string rawBarcode,
        decimal weightKg)
    {
        if (!IsPiece || InventoryMovementId is not null ||
            barcodeProfileId == Guid.Empty || weightKg <= 0 ||
            !ValidQuantity(weightKg, ProductSaleMode.Weight))
            throw new InvalidOperationException("Only a draft traceable line can capture a piece.");
        BarcodeProfileId = barcodeProfileId;
        ReadSourceSystem = Optional(sourceSystem, 120) ?? throw new ArgumentException(
            "Source system is required.", nameof(sourceSystem));
        IdentifierField = Optional(identifierField, 80) ?? throw new ArgumentException(
            "Identifier field is required.", nameof(identifierField));
        ExternalIdentifier = Optional(externalIdentifier, 80) ?? throw new ArgumentException(
            "Piece identifier is required.", nameof(externalIdentifier));
        RawBarcode = Optional(rawBarcode, 80) ?? throw new ArgumentException(
            "Raw barcode is required.", nameof(rawBarcode));
        ReceivedQuantity = weightKg;
    }

    public void MarkSourcePieceVerified()
    {
        if (!IsPiece || !SourcePieceId.HasValue || !HasPhysicalRead)
            throw new InvalidOperationException("A scanned piece and origin id are required.");
        SourcePieceVerified = true;
    }

    internal void MarkApproved() => DifferenceStatus =
        SentQuantity == ReceivedQuantity &&
        (!IsPiece || string.IsNullOrWhiteSpace(SourceExternalIdentifier) ||
         string.Equals(SourceExternalIdentifier, ExternalIdentifier,
             StringComparison.OrdinalIgnoreCase))
            ? InventoryTransferDifferenceStatus.None :
              InventoryTransferDifferenceStatus.PendingReview;

    internal void LinkMovement(Guid movementId, Guid? pieceId)
    {
        if (movementId == Guid.Empty || InventoryMovementId is not null ||
            pieceId == Guid.Empty || pieceId.HasValue != IsPiece)
            throw new InvalidOperationException("A positive receipt line can link one movement and piece.");
        InventoryMovementId = movementId;
        InventoryPieceId = pieceId;
    }

    internal void ResolveDifference(Guid actorUserId, string reason, DateTimeOffset now)
    {
        if (DifferenceStatus != InventoryTransferDifferenceStatus.PendingReview ||
            actorUserId == Guid.Empty || string.IsNullOrWhiteSpace(reason) ||
            reason.Trim().Length > 240)
            throw new InvalidOperationException("A pending difference and reason are required.");
        DifferenceStatus = InventoryTransferDifferenceStatus.Resolved;
        DifferenceReason = reason.Trim();
        DifferenceResolvedByUserId = actorUserId;
        DifferenceResolvedAtUtc = now.ToUniversalTime();
    }

    private static bool ValidQuantity(decimal quantity, ProductSaleMode mode) =>
        quantity is >= 0 and <= 100_000 && decimal.Round(quantity, 3) == quantity &&
        (mode != ProductSaleMode.Unit || quantity == decimal.Truncate(quantity));

    private static string? Optional(string? value, int limit) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= limit
            ? value.Trim() : throw new ArgumentException("Text exceeds its length limit.");
}
