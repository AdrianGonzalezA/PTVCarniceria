using Carnicerias.Central.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carnicerias.Central.Tests;

public sealed class CentralBusinessSummaryServiceTests
{
    private static readonly CentralBusinessWindow Window = new(
        new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 14, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CompleteBranchesSumSalesButKeepCashPerBranch()
    {
        var companyId = Guid.NewGuid();
        var first = Installation(companyId, "Centro");
        var second = Installation(companyId, "Norte");
        var reader = new FixtureReader([first, second]);
        var source = new FixtureSource(new Dictionary<Guid, CentralBranchSummary>
        {
            [first.InstallationId] = Summary(2, 150m, 120m, 50m),
            [second.InstallationId] = Summary(3, 200m, 180m, -5m)
        });
        var service = new CentralBusinessSummaryService(reader, source,
            new FixedTimeProvider(Window.ToUtc), NullLogger<CentralBusinessSummaryService>.Instance);

        var result = await service.ReadAsync(companyId, Window, CancellationToken.None);

        Assert.Equal(companyId, reader.RequestedCompanyId);
        Assert.Equal("complete", result.Completeness);
        Assert.Equal(5, result.Aggregate!.SaleCount);
        Assert.Equal(350m, result.Aggregate.SalesTotal);
        Assert.Equal(300m, result.Aggregate.SalePaymentsTotal);
        Assert.Equal([50m, -5m], result.Branches.Select(branch => branch.OpenShiftCashBalance));
        Assert.Equal(["Centro", "Norte"], result.Branches.Select(branch => branch.Label));
        Assert.DoesNotContain("openShiftCashBalance", System.Text.Json.JsonSerializer.Serialize(result.Aggregate),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OfflineBranchSuppressesAggregateAndDoesNotInventZero()
    {
        var companyId = Guid.NewGuid();
        var first = Installation(companyId, "Centro");
        var second = Installation(companyId, "Norte");
        var reader = new FixtureReader([first, second]);
        var source = new FixtureSource(new Dictionary<Guid, CentralBranchSummary>
        {
            [first.InstallationId] = Summary(2, 150m, 120m, 50m)
        });
        var service = new CentralBusinessSummaryService(reader, source,
            new FixedTimeProvider(Window.ToUtc), NullLogger<CentralBusinessSummaryService>.Instance);

        var result = await service.ReadAsync(companyId, Window, CancellationToken.None);

        Assert.Equal("incomplete", result.Completeness);
        Assert.Null(result.Aggregate);
        Assert.Equal("ok", result.Branches[0].Status);
        Assert.Equal("unavailable", result.Branches[1].Status);
        Assert.Null(result.Branches[1].SalesTotal);
        Assert.Null(result.Branches[1].OpenShiftCashBalance);
    }

    [Fact]
    public async Task NoRegisteredBranchesIsEmptyNotZero()
    {
        var service = new CentralBusinessSummaryService(new FixtureReader([]),
            new FixtureSource([]), new FixedTimeProvider(Window.ToUtc),
            NullLogger<CentralBusinessSummaryService>.Instance);

        var result = await service.ReadAsync(Guid.NewGuid(), Window, CancellationToken.None);

        Assert.Equal("empty", result.Completeness);
        Assert.Empty(result.Branches);
        Assert.Null(result.Aggregate);
    }

    [Fact]
    public async Task DuplicateRemoteBranchCannotBeCountedTwice()
    {
        var companyId = Guid.NewGuid();
        var remoteCompanyId = Guid.NewGuid();
        var remoteBranchId = Guid.NewGuid();
        var first = CentralInstallation.Create(Guid.NewGuid(), companyId,
            remoteCompanyId, remoteBranchId, "Centro A", "https://branch.example/", "BRANCH_TOKEN");
        var second = CentralInstallation.Create(Guid.NewGuid(), companyId,
            remoteCompanyId, remoteBranchId, "Centro B", "https://branch.example/", "BRANCH_TOKEN");
        var source = new FixtureSource(new Dictionary<Guid, CentralBranchSummary>
        {
            [first.InstallationId] = Summary(2, 150m, 120m, 50m),
            [second.InstallationId] = Summary(2, 150m, 120m, 50m)
        });
        var service = new CentralBusinessSummaryService(new FixtureReader([first, second]),
            source, new FixedTimeProvider(Window.ToUtc),
            NullLogger<CentralBusinessSummaryService>.Instance);

        var result = await service.ReadAsync(companyId, Window, CancellationToken.None);

        Assert.Equal("incomplete", result.Completeness);
        Assert.Null(result.Aggregate);
        Assert.All(result.Branches, branch => Assert.Equal("unavailable", branch.Status));
        Assert.Equal(0, source.ReadCount);
    }

    private static CentralInstallation Installation(Guid companyId, string label) =>
        CentralInstallation.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            label, "https://branch.example/", "BRANCH_TOKEN");

    private static CentralBranchSummary Summary(int count, decimal sales, decimal payments,
        decimal cash) => new(Window.ToUtc, count, sales, payments,
            [new CentralPaymentSummary("cash", payments)], 1, cash);

    private sealed class FixtureReader(IReadOnlyList<CentralInstallation> installations)
        : ICentralBusinessInstallationReader
    {
        public Guid RequestedCompanyId { get; private set; }
        public Task<IReadOnlyList<CentralInstallation>> ReadActiveAsync(Guid centralCompanyId,
            CancellationToken cancellationToken)
        {
            RequestedCompanyId = centralCompanyId;
            return Task.FromResult(installations);
        }
    }

    private sealed class FixtureSource(Dictionary<Guid, CentralBranchSummary> summaries)
        : ICentralBranchSummarySource
    {
        public int ReadCount { get; private set; }
        public Task<CentralBranchSummary> ReadAsync(CentralInstallation installation,
            CentralBusinessWindow window, CancellationToken cancellationToken)
        {
            ReadCount++;
            return summaries.TryGetValue(installation.InstallationId, out var summary)
                ? Task.FromResult(summary)
                : throw new InvalidOperationException("offline");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
