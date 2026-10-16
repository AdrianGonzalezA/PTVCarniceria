using Carnicerias.Central.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carnicerias.Central.Tests;

public sealed class CentralBusinessCancellationTests
{
    [Fact]
    public async Task TimedOutBranchBecomesUnavailable()
    {
        var companyId = Guid.NewGuid();
        var installation = CentralInstallation.Create(Guid.NewGuid(), companyId,
            Guid.NewGuid(), Guid.NewGuid(), "Lenta", "https://branch.example/", "BRANCH_TOKEN");
        var service = new CentralBusinessSummaryService(
            new OneInstallationReader(installation), new CancelledSource(),
            TimeProvider.System, NullLogger<CentralBusinessSummaryService>.Instance);
        var window = CentralBusinessWindow.Today(DateTimeOffset.UtcNow);

        var result = await service.ReadAsync(companyId, window, CancellationToken.None);

        Assert.Equal("incomplete", result.Completeness);
        Assert.Null(result.Aggregate);
        Assert.Equal("unavailable", Assert.Single(result.Branches).Status);
    }

    private sealed class OneInstallationReader(CentralInstallation installation)
        : ICentralBusinessInstallationReader
    {
        public Task<IReadOnlyList<CentralInstallation>> ReadActiveAsync(Guid centralCompanyId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CentralInstallation>>([installation]);
    }

    private sealed class CancelledSource : ICentralBranchSummarySource
    {
        public Task<CentralBranchSummary> ReadAsync(CentralInstallation installation,
            CentralBusinessWindow window, CancellationToken cancellationToken) =>
            throw new TaskCanceledException();
    }
}
