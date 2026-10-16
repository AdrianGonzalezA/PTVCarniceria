using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralLiveBranchSummarySourceTests
{
    [Fact]
    public async Task UnapprovedManagedDestinationIsRejectedBeforeCredentialIsResolved()
    {
        var installationId = Guid.NewGuid();
        var installation = CentralInstallation.Create(installationId, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "Sucursal",
            "https://branch.example/", CentralManagedSecretReference.Create(installationId));
        var resolver = new CountingResolver();
        var source = new CentralLiveBranchSummarySource(
            new CentralDestinationPolicy([], false, new CentralDnsResolver()), resolver);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(
            installation, CentralBusinessWindow.Today(DateTimeOffset.UtcNow),
            CancellationToken.None));

        Assert.Equal(0, resolver.Calls);
    }

    private sealed class CountingResolver : ICentralBranchCredentialResolver
    {
        public int Calls { get; private set; }

        public Task<string> ResolveAsync(CentralInstallation installation,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new string('A', 64));
        }
    }
}
