using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralBusinessWindowTests
{
    [Fact]
    public void DefaultIsBuenosAiresCalendarDayToCurrentInstant()
    {
        var instant = new DateTimeOffset(2026, 10, 14, 18, 25, 0, TimeSpan.Zero);

        var window = CentralBusinessWindow.Today(instant);

        Assert.Equal(new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero), window.FromUtc);
        Assert.Equal(instant, window.ToUtc);
    }

    [Fact]
    public void DefaultNearUtcMidnightStillUsesArgentinaDate()
    {
        var instant = new DateTimeOffset(2026, 10, 15, 1, 0, 0, TimeSpan.Zero);

        var window = CentralBusinessWindow.Today(instant);

        Assert.Equal(new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero), window.FromUtc);
        Assert.Equal(instant, window.ToUtc);
    }

    [Theory]
    [InlineData("2026-10-14T03:00:00Z", "2026-10-15T03:00:00Z", true)]
    [InlineData("2026-10-14T03:00:00+00:00", "2026-10-15T03:00:00Z", true)]
    [InlineData("2026-10-14T03:00:00-03:00", "2026-10-15T03:00:00Z", false)]
    [InlineData("2026-10-14T03:00:00Z", "2026-10-14T03:00:00Z", false)]
    [InlineData("2026-10-01T00:00:00Z", "2026-11-02T00:00:00Z", false)]
    [InlineData("garbage", "2026-10-15T03:00:00Z", false)]
    public void ExplicitWindowNeedsUtcAndAtMostThirtyOneDays(
        string fromUtc, string toUtc, bool expected)
    {
        Assert.Equal(expected, CentralBusinessWindow.TryCreate(fromUtc, toUtc, out _));
    }

    [Fact]
    public void DateOnlyRangeUsesInclusiveBuenosAiresCalendarDates()
    {
        Assert.True(CentralBusinessWindow.TryCreateLocalDates(
            "2026-10-14", "2026-10-15", out var window));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero), window.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 3, 0, 0, TimeSpan.Zero), window.ToUtc);
        Assert.False(CentralBusinessWindow.TryCreateLocalDates(
            "2026-10-15", "2026-10-14", out _));
        Assert.False(CentralBusinessWindow.TryCreateLocalDates(
            "2026-10-01", "2026-11-01", out _));
    }
}
