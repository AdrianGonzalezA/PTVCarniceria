namespace Carnicerias.Infrastructure;

public enum CustomerCollectionCorrectionKind
{
    Reallocate,
    Refund
}

public sealed class CustomerCollectionCorrection
{
    private CustomerCollectionCorrection() => Reason = RequestHash = string.Empty;

    public CustomerCollectionCorrection(Guid companyId, Guid branchId, Guid originalReceiptId,
        Guid? replacementReceiptId, Guid cashierId, Guid cashierShiftId, Guid? posTerminalId,
        Guid operationId, string requestHash, CustomerCollectionCorrectionKind kind,
        string reason, decimal amount, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || originalReceiptId == Guid.Empty ||
            replacementReceiptId == Guid.Empty || cashierId == Guid.Empty || cashierShiftId == Guid.Empty ||
            posTerminalId == Guid.Empty || operationId == Guid.Empty || requestHash.Length != 64 ||
            !Enum.IsDefined(kind) || (kind == CustomerCollectionCorrectionKind.Reallocate) !=
            replacementReceiptId.HasValue || string.IsNullOrWhiteSpace(reason) || reason.Length > 240 ||
            amount <= 0 || amount > 9_999_999_999.99m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("A valid correction, reason and receipt references are required.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        OriginalReceiptId = originalReceiptId;
        ReplacementReceiptId = replacementReceiptId;
        CashierId = cashierId;
        CashierShiftId = cashierShiftId;
        PosTerminalId = posTerminalId;
        OperationId = operationId;
        RequestHash = requestHash;
        Kind = kind;
        Reason = reason.Trim();
        Amount = amount;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public long CorrectionNumber { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OriginalReceiptId { get; private set; }
    public Guid? ReplacementReceiptId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public Guid? PosTerminalId { get; private set; }
    public Guid OperationId { get; private set; }
    public string RequestHash { get; private set; }
    public CustomerCollectionCorrectionKind Kind { get; private set; }
    public string Reason { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
