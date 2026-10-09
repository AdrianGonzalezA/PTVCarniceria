using Carnicerias.Api.History;

namespace Carnicerias.IntegrationTests;

public sealed class BusinessSummaryWindowTests
{
    [Fact]
    public void AcceptsAnExclusiveUtcDay()
    {
        var from = new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero);
        Assert.True(BusinessSummaryWindow.TryCreate(from, from.AddDays(1), out var window));
        Assert.Equal(from, window.FromUtc);
        Assert.Equal(from.AddDays(1), window.ToUtc);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 367)]
    public void RejectsMissingReversedOrUnboundedRanges(int fromOffsetDays, int toOffsetDays)
    {
        var start = new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero);
        Assert.False(BusinessSummaryWindow.TryCreate(start.AddDays(fromOffsetDays),
            start.AddDays(toOffsetDays), out _));
    }

    [Fact]
    public void RejectsNonUtcOffsetsAndMissingDates()
    {
        var start = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(-3));
        Assert.False(BusinessSummaryWindow.TryCreate(start, start.AddDays(1), out _));
        Assert.False(BusinessSummaryWindow.TryCreate(null, start.ToUniversalTime(), out _));
    }

    [Fact]
    public void ArgentinaDayUsesTheLocalMidnightBoundaries()
    {
        var window = BusinessSummaryWindow.ForArgentinaDate(new DateOnly(2026, 10, 8));
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero), window.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 3, 0, 0, TimeSpan.Zero), window.ToUtc);
    }
}
