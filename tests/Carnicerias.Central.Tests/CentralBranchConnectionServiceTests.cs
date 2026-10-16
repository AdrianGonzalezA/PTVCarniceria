using System.Net;
using System.Security.Cryptography;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralBranchConnectionServiceTests
{
    private const string Token = "5C404463E2888939B5D4D8065233A8BBC8B92525279AA48E409F4982D5F30A90";

    [Fact]
    public void ManagementScopeIncludesInactiveInstallationsOnlyForTheSessionCompany()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var a = CentralInstallation.Create(Guid.NewGuid(), companyA, Guid.NewGuid(),
            Guid.NewGuid(), "A", "https://branch-a.example/", "A_TOKEN");
        a.Deactivate();
        var b = CentralInstallation.Create(Guid.NewGuid(), companyB, Guid.NewGuid(),
            Guid.NewGuid(), "B", "https://branch-b.example/", "B_TOKEN");

        var result = CentralBranchConnectionQuery.Scope(new[] { a, b }.AsQueryable(),
            companyA).ToArray();

        Assert.Equal(a.InstallationId, Assert.Single(result).InstallationId);
    }

    [Fact]
    public void ActiveRemotePairIsGloballyUniqueAcrossCentralCompanies()
    {
        var remoteCompany = Guid.NewGuid();
        var remoteBranch = Guid.NewGuid();
        var a = CentralInstallation.Create(Guid.NewGuid(), Guid.NewGuid(), remoteCompany,
            remoteBranch, "A", "https://branch.example/", "A_TOKEN");
        var b = CentralInstallation.Create(Guid.NewGuid(), Guid.NewGuid(), remoteCompany,
            remoteBranch, "B", "https://branch.example/", "B_TOKEN");
        var unrelated = CentralInstallation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "C", "https://branch.example/", "C_TOKEN");

        var matches = CentralBranchConnectionQuery.ActiveRemotePair(
            new[] { a, b, unrelated }.AsQueryable(), remoteCompany, remoteBranch).ToArray();

        Assert.Equal(2, matches.Length);
    }

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

    [Fact]
    public async Task CheckRecordsFailureWithoutChangingAdministrativeState()
    {
        var companyId = Guid.NewGuid();
        var request = Request();
        var store = new FakeStore();
        var verifier = new FakeVerifier();
        var service = Service(companyId, store, verifier, Crypto());
        await service.RegisterAsync(companyId, Guid.NewGuid(), request, CancellationToken.None);
        store.Installations[0].Deactivate();
        verifier.Reject = true;

        var result = await service.CheckAsync(companyId, Guid.NewGuid(), request.InstallationId,
            CancellationToken.None);

        Assert.False(result.IsActive);
        Assert.Equal("unavailable", result.LastCheckStatus);
        Assert.NotNull(result.LastCheckedAtUtc);
        Assert.Equal("unavailable", store.Installations[0].LastCheckStatus);
    }

    [Fact]
    public async Task OtherCompanyCannotCheckOrEditKnownInstallation()
    {
        var companyId = Guid.NewGuid();
        var request = Request();
        var store = new FakeStore();
        var service = Service(companyId, store, new FakeVerifier(), Crypto());
        await service.RegisterAsync(companyId, Guid.NewGuid(), request, CancellationToken.None);

        await Assert.ThrowsAsync<CentralBranchConnectionNotFoundException>(() =>
            service.CheckAsync(Guid.NewGuid(), Guid.NewGuid(), request.InstallationId,
                CancellationToken.None));
        await Assert.ThrowsAsync<CentralBranchConnectionNotFoundException>(() =>
            service.EditAsync(Guid.NewGuid(), Guid.NewGuid(), request.InstallationId,
                new CentralBranchConnectionEdit("Other", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task FailedTokenRotationPreservesThePreviousCredentialAndUrl()
    {
        var companyId = Guid.NewGuid();
        var request = Request();
        var store = new FakeStore();
        var verifier = new FakeVerifier();
        var crypto = Crypto();
        var service = Service(companyId, store, verifier, crypto);
        await service.RegisterAsync(companyId, Guid.NewGuid(), request, CancellationToken.None);
        verifier.Reject = true;

        await Assert.ThrowsAsync<CentralBranchConnectionVerificationException>(() =>
            service.EditAsync(companyId, Guid.NewGuid(), request.InstallationId,
                new CentralBranchConnectionEdit("Changed", "https://branch.example/", new string('B', 64)),
                CancellationToken.None));

        Assert.Equal(request.Label, store.Installations[0].Label);
        Assert.Equal(request.DestinationUrl, store.Installations[0].DestinationUrl);
        Assert.Equal(Token, crypto.Decrypt(companyId, request.InstallationId,
            Assert.Single(store.Credentials).ToEnvelope()));
    }

    [Fact]
    public async Task ReactivationRequiresVerificationAndIsIdempotent()
    {
        var companyId = Guid.NewGuid();
        var request = Request();
        var store = new FakeStore();
        var verifier = new FakeVerifier();
        var service = Service(companyId, store, verifier, Crypto());
        await service.RegisterAsync(companyId, Guid.NewGuid(), request, CancellationToken.None);
        var actor = Guid.NewGuid();

        var disabled = await service.ChangeStateAsync(companyId, actor, request.InstallationId,
            false, CancellationToken.None);
        Assert.False(disabled.IsActive);
        verifier.Reject = true;
        await Assert.ThrowsAsync<CentralBranchConnectionVerificationException>(() =>
            service.ChangeStateAsync(companyId, actor, request.InstallationId,
                true, CancellationToken.None));
        Assert.False(store.Installations[0].IsActive);
        verifier.Reject = false;
        var restored = await service.ChangeStateAsync(companyId, actor, request.InstallationId,
            true, CancellationToken.None);
        Assert.True(restored.IsActive);
        var calls = verifier.Calls;
        await service.ChangeStateAsync(companyId, actor, request.InstallationId,
            true, CancellationToken.None);
        Assert.Equal(calls, verifier.Calls);
    }

    [Fact]
    public async Task SuccessfulTokenRotationReplacesCiphertextAndKeepsRemoteIdentity()
    {
        var companyId = Guid.NewGuid();
        var request = Request();
        var store = new FakeStore();
        var crypto = Crypto();
        var service = Service(companyId, store, new FakeVerifier(), crypto);
        await service.RegisterAsync(companyId, Guid.NewGuid(), request, CancellationToken.None);

        var rotated = await service.EditAsync(companyId, Guid.NewGuid(), request.InstallationId,
            new CentralBranchConnectionEdit(null, null, new string('B', 64)),
            CancellationToken.None);

        Assert.Equal(request.RemoteCompanyId, rotated.RemoteCompanyId);
        Assert.Equal(request.RemoteBranchId, rotated.RemoteBranchId);
        Assert.Equal(new string('B', 64), crypto.Decrypt(companyId,
            request.InstallationId, Assert.Single(store.Credentials).ToEnvelope()));
        Assert.Equal("available", rotated.LastCheckStatus);
        Assert.DoesNotContain(new string('B', 64), rotated.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyConnectionUsesTheOriginalGlobalApprovalForChecks()
    {
        var companyId = Guid.NewGuid();
        var installation = CentralInstallation.Create(Guid.NewGuid(), companyId,
            Guid.NewGuid(), Guid.NewGuid(), "Legacy", "https://branch.example/", "OLD_TOKEN");
        var store = new FakeStore();
        store.Installations.Add(installation);
        var verifier = new FakeVerifier();
        var policy = new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri("https://branch.example/"),
                [IPAddress.Parse("10.10.1.25")])], false, new FakeDns());
        var service = new CentralBranchConnectionService(store, verifier, policy,
            new FakeResolver(store, Crypto()), Crypto, TimeProvider.System);

        var checkedConnection = await service.CheckAsync(companyId, Guid.NewGuid(),
            installation.InstallationId, CancellationToken.None);

        Assert.Equal("available", checkedConnection.LastCheckStatus);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task CheckDoesNotClaimAStoredCredentialThatIsMissing()
    {
        var companyId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var installation = CentralInstallation.Create(installationId, companyId,
            Guid.NewGuid(), Guid.NewGuid(), "Missing", "https://branch.example/",
            CentralManagedSecretReference.Create(installationId));
        var store = new FakeStore();
        store.Installations.Add(installation);
        var service = Service(companyId, store, new FakeVerifier(), Crypto());

        var result = await service.CheckAsync(companyId, Guid.NewGuid(), installationId,
            CancellationToken.None);

        Assert.False(result.HasCredential);
        Assert.Equal("unavailable", result.LastCheckStatus);
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
            new FakeResolver(store, crypto), () => crypto, TimeProvider.System);
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

        public Task<CentralBranchConnectionSnapshot?> FindAsync(Guid companyId, Guid installationId,
            CancellationToken cancellationToken)
        {
            var installation = Installations.SingleOrDefault(item =>
                item.CentralCompanyId == companyId && item.InstallationId == installationId);
            return Task.FromResult(installation is null ? null :
                new CentralBranchConnectionSnapshot(installation,
                    Credentials.Any(item => item.InstallationId == installationId)
                    || !CentralManagedSecretReference.IsManaged(installation)));
        }

        public Task SaveChangedAsync(CentralInstallation installation,
            CentralBranchCredential? credential, CentralAuditEvent audit,
            long expectedRevision, CancellationToken cancellationToken)
        {
            var index = Installations.FindIndex(item => item.InstallationId == installation.InstallationId);
            if (index < 0 || Installations[index].Revision <= expectedRevision)
                throw new CentralBranchConnectionConflictException();
            Installations[index] = installation;
            if (credential is not null)
            {
                Credentials.RemoveAll(item => item.InstallationId == installation.InstallationId);
                Credentials.Add(credential);
            }
            Audits.Add(audit);
            return Task.CompletedTask;
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

    private sealed class FakeResolver(FakeStore store, CentralBranchCredentialCrypto crypto)
        : ICentralBranchCredentialResolver
    {
        public Task<string> ResolveAsync(CentralInstallation installation,
            CancellationToken cancellationToken)
        {
            var credential = store.Credentials.SingleOrDefault(item =>
                item.InstallationId == installation.InstallationId);
            if (credential is null && !CentralManagedSecretReference.IsManaged(installation))
                return Task.FromResult(Token);
            if (credential is null) throw new InvalidOperationException("Missing credential.");
            return Task.FromResult(crypto.Decrypt(installation.CentralCompanyId,
                installation.InstallationId, credential.ToEnvelope()));
        }
    }
}
