using System.Net;
using Carnicerias.Central.Bootstrap;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralRegistrationTests
{
    private const string Destination = "http://127.0.0.1:5246/";
    private const string Secret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task AcceptsOnlyVerifiedIdentityAndStoresReferenceNotCredential()
    {
        var store = new FakeStore();
        var probe = new FakeProbe { Accept = true };
        var service = CreateService(store, probe, Secret);
        var request = Request();

        var result = await service.RegisterAsync(request, CancellationToken.None);

        Assert.Equal(request.InstallationId, result);
        var installation = Assert.Single(store.Installations);
        Assert.Equal(store.Company.Id, installation.CentralCompanyId);
        Assert.Equal("BRANCH_CENTRO", installation.SecretReference);
        Assert.DoesNotContain(Secret, installation.SecretReference, StringComparison.Ordinal);
        Assert.Equal(CentralAuditAction.InstallationRegistrationAccepted,
            Assert.Single(store.Audits).Action);
        Assert.Equal(Secret, probe.ReceivedSecret);
        Assert.Equal(request.RemoteCompanyId, probe.ReceivedCompanyId);
    }

    [Fact]
    public async Task RejectedRemoteIdentityNeverCreatesInstallationAndAuditsWithoutSecret()
    {
        var store = new FakeStore();
        var probe = new FakeProbe();
        var service = CreateService(store, probe, Secret);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterAsync(Request(), CancellationToken.None));

        Assert.Empty(store.Installations);
        var audit = Assert.Single(store.Audits);
        Assert.Equal(CentralAuditAction.InstallationRegistrationRejected, audit.Action);
        Assert.Equal(store.Company.Id, audit.CentralCompanyId);
        Assert.DoesNotContain(typeof(CentralAuditEvent).GetProperties(),
            property => property.PropertyType == typeof(string));
    }

    [Fact]
    public async Task MissingSecretDuplicateAndInactiveCompanyFailBeforeProbe()
    {
        var store = new FakeStore();
        var probe = new FakeProbe { Accept = true };
        var request = Request();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(store, probe, null).RegisterAsync(request, CancellationToken.None));
        Assert.Equal(0, probe.Calls);
        Assert.Empty(store.Installations);

        await CreateService(store, probe, Secret).RegisterAsync(request, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(store, probe, Secret).RegisterAsync(request, CancellationToken.None));
        Assert.Equal(1, probe.Calls);
        Assert.Single(store.Installations);

        store.Company.Deactivate();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(store, probe, Secret).RegisterAsync(Request(), CancellationToken.None));
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void ProtectedApprovedEndpointConfigurationRejectsMissingOrMalformedLists()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CentralRegistrationCommand.ParseApprovedEndpoints(null));
        Assert.Throws<InvalidOperationException>(() =>
            CentralRegistrationCommand.ParseApprovedEndpoints("[]"));
        Assert.Throws<InvalidOperationException>(() =>
            CentralRegistrationCommand.ParseApprovedEndpoints(
                "[{\"url\":\"https://branch.example/\",\"addresses\":[\"not-an-ip\"]}]"));

        var approved = CentralRegistrationCommand.ParseApprovedEndpoints(
            "[{\"url\":\"https://branch.example:8443/\",\"addresses\":[\"10.10.1.25\"]}]");
        Assert.Equal("https://branch.example:8443/", Assert.Single(approved).Destination.AbsoluteUri);
    }

    [Fact]
    public void ApprovedEndpointConfigurationPreservesCentralCompanyScope()
    {
        var companyId = Guid.NewGuid();
        var approved = CentralApprovedEndpointConfiguration.Parse(
            $"[{{\"url\":\"https://branch.example/\",\"addresses\":[\"10.10.1.25\"],\"centralCompanyId\":\"{companyId}\"}}]");

        Assert.Equal(companyId, Assert.Single(approved).CentralCompanyId);
        Assert.Throws<InvalidOperationException>(() => CentralApprovedEndpointConfiguration.Parse(
            "[{\"url\":\"https://branch.example/\",\"addresses\":[\"10.10.1.25\"],\"centralCompanyId\":\"00000000-0000-0000-0000-000000000000\"}]"));
    }

    private static CentralRegistrationRequest Request() => new(
        "VISUAL", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Sucursal Centro", Destination, "branch_centro");

    private static CentralRegistrationService CreateService(
        FakeStore store, FakeProbe probe, string? secret)
    {
        var policy = new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri(Destination), [IPAddress.Loopback])],
            true, new CentralDnsResolver());
        return new CentralRegistrationService(store, new FakeSecretSource(secret),
            probe, policy, TimeProvider.System);
    }

    private sealed class FakeStore : ICentralRegistrationStore
    {
        public CentralCompany Company { get; } = CentralCompany.Create("VISUAL", "Visual");
        public List<CentralInstallation> Installations { get; } = [];
        public List<CentralAuditEvent> Audits { get; } = [];

        public Task<CentralCompany?> FindCompanyAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(code == Company.Code ? Company : null);

        public Task<bool> InstallationExistsAsync(Guid installationId, CancellationToken cancellationToken) =>
            Task.FromResult(Installations.Any(item => item.InstallationId == installationId));

        public Task SaveAcceptedAsync(CentralInstallation installation, CentralAuditEvent audit,
            CancellationToken cancellationToken)
        {
            Installations.Add(installation);
            Audits.Add(audit);
            return Task.CompletedTask;
        }

        public Task SaveRejectedAsync(CentralAuditEvent audit, CancellationToken cancellationToken)
        {
            Audits.Add(audit);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSecretSource(string? secret) : ICentralBranchSecretSource
    {
        public string? Read(string reference) => secret;
    }

    private sealed class FakeProbe : ICentralBranchProbe
    {
        public bool Accept { get; set; }
        public int Calls { get; private set; }
        public string? ReceivedSecret { get; private set; }
        public Guid ReceivedCompanyId { get; private set; }

        public Task VerifyAsync(CentralApprovedDestination approved, string secret,
            Guid installationId, Guid companyId, Guid branchId,
            CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedSecret = secret;
            ReceivedCompanyId = companyId;
            if (!Accept) throw new InvalidOperationException("Remote identity mismatch.");
            return Task.CompletedTask;
        }
    }
}
