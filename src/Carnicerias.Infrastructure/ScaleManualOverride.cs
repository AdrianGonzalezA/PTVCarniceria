namespace Carnicerias.Infrastructure;

/// <summary>An on-site PIN approval for one manual weight on a terminal with an assigned scale.</summary>
public sealed class ScaleManualOverride
{
    private ScaleManualOverride() { Reason = string.Empty; }

    public ScaleManualOverride(Guid companyId, Guid branchId, Guid terminalId,
        Guid cashierShiftId, Guid cashierId, Guid productId, Guid authorizerId,
        string reason, DateTimeOffset authorizedAtUtc)
    {
        if (new[] { companyId, branchId, terminalId, cashierShiftId, cashierId,
                productId, authorizerId }.Any(id => id == Guid.Empty) ||
            cashierId == authorizerId || string.IsNullOrWhiteSpace(reason) ||
            reason.Trim().Length is < 10 or > 200)
            throw new ArgumentException("A distinct authorizer and a reason are required.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        PosTerminalId = terminalId;
        CashierShiftId = cashierShiftId;
        CashierId = cashierId;
        ProductId = productId;
        AuthorizedByUserId = authorizerId;
        Reason = reason.Trim();
        AuthorizedAtUtc = authorizedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid PosTerminalId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid AuthorizedByUserId { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset AuthorizedAtUtc { get; private set; }
    public Guid? AppliedSaleDraftId { get; private set; }
    public decimal? AppliedWeightKg { get; private set; }
    public DateTimeOffset? AppliedAtUtc { get; private set; }

    public bool CanApply(Guid draftId, Guid productId, decimal weightKg, DateTimeOffset now) =>
        draftId != Guid.Empty && productId == ProductId && weightKg > 0 &&
        decimal.Round(weightKg, 3) == weightKg &&
        AppliedSaleDraftId is null && now >= AuthorizedAtUtc &&
        now - AuthorizedAtUtc <= TimeSpan.FromMinutes(15);

    public void Apply(Guid draftId, Guid productId, decimal weightKg, DateTimeOffset now)
    {
        if (!CanApply(draftId, productId, weightKg, now))
            throw new InvalidOperationException("The manual weight approval cannot be applied.");
        AppliedSaleDraftId = draftId;
        AppliedWeightKg = weightKg;
        AppliedAtUtc = now.ToUniversalTime();
    }
}
