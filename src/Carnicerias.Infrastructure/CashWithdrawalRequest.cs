using Carnicerias.Domain.Sales;

namespace Carnicerias.Infrastructure;

public enum CashWithdrawalStatus { Pending, Approved, Rejected }

/// <summary>A cashier's request for a separately approved cash removal.</summary>
public sealed class CashWithdrawalRequest
{
    private CashWithdrawalRequest() { Reason = string.Empty; }

    public CashWithdrawalRequest(Guid companyId, Guid branchId, Guid? posTerminalId,
        Guid cashierShiftId, Guid requestedByUserId, Guid operationId, decimal amount,
        string reason, DateTimeOffset requestedAtUtc)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || posTerminalId == Guid.Empty ||
            cashierShiftId == Guid.Empty || requestedByUserId == Guid.Empty ||
            operationId == Guid.Empty || amount <= 0 || amount > 9_999_999_999.99m ||
            decimal.Round(amount, 2) != amount || string.IsNullOrWhiteSpace(reason) ||
            reason.Trim().Length > 500)
            throw new ArgumentException("A valid cash withdrawal request is required.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        PosTerminalId = posTerminalId;
        CashierShiftId = cashierShiftId;
        RequestedByUserId = requestedByUserId;
        OperationId = operationId;
        Method = PaymentMethod.Cash;
        Amount = amount;
        Reason = reason.Trim();
        RequestedAtUtc = requestedAtUtc.ToUniversalTime();
        Status = CashWithdrawalStatus.Pending;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? PosTerminalId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid OperationId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public CashWithdrawalStatus Status { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public Guid? LedgerMovementId { get; private set; }

    public void Approve(Guid administratorId, Guid ledgerMovementId, decimal availableCash,
        DateTimeOffset decidedAtUtc)
    {
        RequireIndependentPendingDecision(administratorId, decidedAtUtc);
        if (availableCash < Amount)
            throw new InvalidOperationException("Insufficient recorded cash for withdrawal.");
        if (ledgerMovementId == Guid.Empty)
            throw new ArgumentException("Ledger movement is required.", nameof(ledgerMovementId));
        Status = CashWithdrawalStatus.Approved;
        DecidedByUserId = administratorId;
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
        LedgerMovementId = ledgerMovementId;
    }

    public void Reject(Guid administratorId, string reason, DateTimeOffset decidedAtUtc)
    {
        RequireIndependentPendingDecision(administratorId, decidedAtUtc);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("A rejection reason is required.", nameof(reason));
        Status = CashWithdrawalStatus.Rejected;
        DecidedByUserId = administratorId;
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
        DecisionReason = reason.Trim();
    }

    private void RequireIndependentPendingDecision(Guid administratorId, DateTimeOffset decidedAtUtc)
    {
        if (Status != CashWithdrawalStatus.Pending || administratorId == Guid.Empty ||
            administratorId == RequestedByUserId || decidedAtUtc < RequestedAtUtc)
            throw new InvalidOperationException("A different administrator must decide a pending request.");
    }
}
