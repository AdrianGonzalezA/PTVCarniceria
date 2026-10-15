using System.Net;
using System.Security.Cryptography;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralBranchConnectionServiceTests
{
    private const string Token = "5C404463E2888939B5D4D8065233A8BBC8B92525279AA48E409F4982D5F30A90";

    [Fact]
    public async Task RegistrationVerifiesRemoteIdentityThenStoresEncryptedCredential()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var request = Request();
        var store = new FakeStore();
        var verifier = new FakeVerifier();
        var crypto = Crypto();
        var service = Service(companyId, store, verifier, crypto);

        var result = await service.RegisterAsync(companyId, actorId, request,
            CancellationToken.None);

        Assert.Equal(request.InstallationId, result.InstallationId);
        Assert.Equal(companyId, Assert.Single(store.Installations).CentralCompanyId);
        Assert.Equal(1, verifier.Calls);
        Assert.Equal(Token, verifier.Token);
        Assert.Equal(Token, crypto.Decrypt(companyId, request.InstallationId,
            Assert.Single(store.Credentials).ToEnvelope()));
        Assert.Equal(actorId, Assert.Single(store.Audits).ActorId);
        Assert.DoesNotContain(Token, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherCompanyDestinationAndRemoteMismatchNeverPersistConnection()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var request = Request();
        var store = new FakeStore();
        var verifier = new FakeVerifier();
        var service = Service(companyA, store, verifier, Crypto());

        await Assert.ThrowsAsync<CentralBranchConnectionVerificationException>(() =>
            service.RegisterAsync(companyB, Guid.NewGuid(), request, CancellationToken.None));
        Assert.Empty(store.Installations);
        Assert.Equal(0, verifier.Calls);

        verifier.Reject = true;
        await Assert.ThrowsAsync<CentralBranchConnectionVerificationException>(() =>
            service.RegisterAsync(companyA, Guid.NewGuid(), request, CancellationToken.None));
        Assert.Empty(store.Installations);
        Assert.Empty(store.Credentials);
        Assert.Equal(2, store.Audits.Count);
    }

    [Fact]
    public async Task ListPassesTheSessionCompanyToTheStore()
    {
        var companyId = Guid.NewGuid();
        var store = new FakeStore();
        var service = Service(companyId, store, new FakeVerifier(), Crypto());

        var page = await service.ListAsync(companyId, 2, 20, CancellationToken.None);

        Assert.Equal(companyId, store.LastListedCompanyId);
        Assert.Equal(2, page.Page);
    }

    private static CentralBranchConnectionCreate Request() => new(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "Sucursal", "https://branch.example/", Token);

    private static CentralBranchConnectionService Service(Guid allowedCompany,
        FakeStore store, FakeVerifier verifier, CentralBranchCredentialCrypto crypto)
    {
        var policy = new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri("https://branch.example/"),
                [IPAddress.Parse("10.10.1.25")], allowedCompany)],
            false, new FakeDns());
        return new CentralBranchConnectionService(store, verifier, policy,
            () => crypto, TimeProvider.System);
    }

    private static CentralBranchCredentialCrypto Crypto()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        return new CentralBranchCredentialCrypto(1, version => version == 1 ? key : null);
    }

    private sealed class FakeDns : ICentralDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { IPAddress.Parse("10.10.1.25") });
    }

    private sealed class FakeVerifier : ICentralInstallationVerifier
    {
        public int Calls { get; private set; }
        public bool Reject { get; set; }
        public string? Token { get; private set; }

        public Task VerifyAsync(CentralApprovedDestination destination, string token,
            Guid installationId, Guid companyId, Guid branchId,
            CancellationToken cancellationToken)
        {
            Calls++;
            Token = token;
            if (Reject) throw new InvalidOperationException("Remote identity mismatch.");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : ICentralBranchConnectionStore
    {
        public List<CentralInstallation> Installations { get; } = [];
        public List<CentralBranchCredential> Credentials { get; } = [];
        public List<CentralAuditEvent> Audits { get; } = [];
        public Guid LastListedCompanyId { get; private set; }

        public Task<CentralManagementPage<CentralBranchConnectionView>> ListAsync(
            Guid centralCompanyId, int page, int pageSize, CancellationToken cancellationToken)
        {
            LastListedCompanyId = centralCompanyId;
            return Task.FromResult(new CentralManagementPage<CentralBranchConnectionView>(
                [], page, pageSize, 0));
        }

        public Task SaveRegisteredAsync(CentralInstallation installation,
            CentralBranchCredential credential, CentralAuditEvent audit,
            CancellationToken cancellationToken)
        {
            Installations.Add(installation);
            Credentials.Add(credential);
            Audits.Add(audit);
            return Task.CompletedTask;
        }

        public Task RecordRejectedAsync(CentralAuditEvent audit,
            CancellationToken cancellationToken)
        {
            Audits.Add(audit);
            return Task.CompletedTask;
        }
    }
}
