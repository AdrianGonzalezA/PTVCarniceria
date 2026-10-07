namespace Carnicerias.Infrastructure;

public enum CashierShiftStatus
{
    Open,
    Closed
}

public sealed class CashierShift
{
    private CashierShift() { }

    public CashierShift(
        Guid companyId,
        Guid branchId,
        Guid cashierId,
        decimal openingCash,
        DateTimeOffset openedAtUtc,
        Guid? posTerminalId = null)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || cashierId == Guid.Empty)
            throw new ArgumentException("Company, branch and cashier ids are required.");
        if (openingCash < 0 || openingCash > 9_999_999_999.99m)
            throw new ArgumentOutOfRangeException(nameof(openingCash));
        if (posTerminalId == Guid.Empty)
            throw new ArgumentException("Terminal id cannot be empty.", nameof(posTerminalId));
        ArgumentOutOfRangeException.ThrowIfNotEqual(
            openingCash, decimal.Round(openingCash, 2), nameof(openingCash));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        CashierId = cashierId;
        PosTerminalId = posTerminalId;
        OpeningCash = openingCash;
        OpenedAtUtc = openedAtUtc.ToUniversalTime();
        Status = CashierShiftStatus.Open;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid? PosTerminalId { get; private set; }
    public decimal OpeningCash { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public CashierShiftStatus Status { get; private set; }

    public void Close(Guid cashierId, DateTimeOffset closedAtUtc)
    {
        if (Status != CashierShiftStatus.Open)
            throw new InvalidOperationException("Only an open shift can be closed.");
        if (cashierId != CashierId)
            throw new InvalidOperationException("Only the owning cashier can close this shift.");
        ArgumentOutOfRangeException.ThrowIfLessThan(closedAtUtc, OpenedAtUtc, nameof(closedAtUtc));

        ClosedAtUtc = closedAtUtc.ToUniversalTime();
        Status = CashierShiftStatus.Closed;
    }
}
