namespace Carnicerias.Infrastructure;

// A closing report is written once, in the same transaction as its shift closing.
// The versioned JSON preserves the exact figures shown to the cashier and administrator.
public sealed class ShiftClosingReport
{
    private ShiftClosingReport() { }

    public ShiftClosingReport(Guid companyId, Guid cashierShiftId, string snapshotJson,
        DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || cashierShiftId == Guid.Empty ||
            string.IsNullOrWhiteSpace(snapshotJson))
            throw new ArgumentException("A company, shift and snapshot are required.");
        CompanyId = companyId;
        CashierShiftId = cashierShiftId;
        SnapshotJson = snapshotJson;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid CompanyId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
