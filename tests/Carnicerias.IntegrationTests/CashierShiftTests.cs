using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class CashierShiftTests
{
    [Fact]
    public void OpensWithZeroOpeningCashAndNormalizesTimestampToUtc()
    {
        var openedAt = new DateTimeOffset(2026, 10, 6, 13, 0, 0, TimeSpan.FromHours(-3));
        var shift = new CashierShift(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m, openedAt);

        Assert.Equal(CashierShiftStatus.Open, shift.Status);
        Assert.Equal(0m, shift.OpeningCash);
        Assert.Equal(openedAt.ToUniversalTime(), shift.OpenedAtUtc);
        Assert.Null(shift.ClosedAtUtc);
    }

    [Fact]
    public void RejectsNegativeOpeningCash()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            new CashierShift(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -0.01m, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void ClosesAnOpenShiftAndRejectsClosingItTwice()
    {
        var cashierId = Guid.NewGuid();
        var shift = new CashierShift(Guid.NewGuid(), Guid.NewGuid(), cashierId, 1500m, DateTimeOffset.UtcNow);
        var closedAt = DateTimeOffset.UtcNow.AddHours(8);

        shift.Close(cashierId, closedAt);

        Assert.Equal(CashierShiftStatus.Closed, shift.Status);
        Assert.Equal(closedAt.ToUniversalTime(), shift.ClosedAtUtc);
        Assert.Throws<InvalidOperationException>(() => shift.Close(cashierId, closedAt.AddMinutes(1)));
    }
}
