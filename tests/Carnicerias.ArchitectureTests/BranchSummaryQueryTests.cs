using Carnicerias.Api.Federation;
using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class BranchSummaryQueryTests
{
    [Fact]
    public void ScopesSalesByBothCompanyAndBranchAndHalfOpenUtcWindow()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var window = new BranchSummaryWindow(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
        ConfirmedSale Sale(Guid company, Guid branch, DateTimeOffset at) =>
            new(company, branch, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), 100m, "test", at);
        var rows = new[]
        {
            Sale(companyId, branchId, window.FromUtc),
            Sale(companyId, branchId, window.ToUtc),
            Sale(companyId, Guid.NewGuid(), window.FromUtc),
            Sale(Guid.NewGuid(), branchId, window.FromUtc)
        };

        var scoped = BranchSummaryQuery.ScopeSales(rows.AsQueryable(), companyId, branchId, window)
            .ToArray();

        Assert.Single(scoped);
        Assert.Same(rows[0], scoped[0]);
    }

    [Fact]
    public void ScopesOpenShiftsByBothCompanyAndBranch()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        CashierShift Shift(Guid company, Guid branch) =>
            new(company, branch, Guid.NewGuid(), 100m, DateTimeOffset.UtcNow);
        var open = Shift(companyId, branchId);
        var closed = Shift(companyId, branchId);
        closed.Close(closed.CashierId, closed.OpenedAtUtc);
        var rows = new[]
        {
            open, closed, Shift(companyId, Guid.NewGuid()), Shift(Guid.NewGuid(), branchId)
        };

        var scoped = BranchSummaryQuery.ScopeOpenShifts(rows.AsQueryable(), companyId, branchId)
            .ToArray();

        Assert.Single(scoped);
        Assert.Same(open, scoped[0]);
    }
}
