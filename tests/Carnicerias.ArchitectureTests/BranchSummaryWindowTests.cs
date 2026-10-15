using Carnicerias.Api.Federation;

namespace Carnicerias.ArchitectureTests;

public sealed class BranchSummaryWindowTests
{
    [Fact]
    public void AcceptsAnExplicitUtcWindowOfAtMostThirtyOneDays()
    {
        Assert.True(BranchSummaryWindow.TryCreate("2026-10-01T00:00:00Z",
            "2026-11-01T00:00:00+00:00", out var window));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), window.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), window.ToUtc);
    }

    [Theory]
    [InlineData(null, "2026-10-02T00:00:00Z")]
    [InlineData("2026-10-01T00:00:00", "2026-10-02T00:00:00Z")]
    [InlineData("2026-10-01T00:00:00-03:00", "2026-10-02T00:00:00Z")]
    [InlineData("2026-10-1T00:00:00Z", "2026-10-02T00:00:00Z")]
    [InlineData("2026-10-02T00:00:00Z", "2026-10-02T00:00:00Z")]
    [InlineData("2026-10-02T00:00:00Z", "2026-10-01T00:00:00Z")]
    [InlineData("2026-10-01T00:00:00Z", "2026-11-01T00:00:01Z")]
    public void RejectsMissingNonUtcOrOutOfRangeWindows(string? fromUtc, string? toUtc)
    {
        Assert.False(BranchSummaryWindow.TryCreate(fromUtc, toUtc, out _));
    }
}
