using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralSessionServiceTests
{
    [Fact]
    public async Task UnknownAccountAndWrongPasswordReceiveSameFailureAndOneVerification()
    {
        var (company, administrator, store, hasher, clock) = CreateFixture();
        var service = new CentralSessionService(store, hasher, clock);

        var wrongPassword = await service.LoginAsync(company.Code, administrator.Username,
            "the wrong password", CancellationToken.None);
        var unknownAccount = await service.LoginAsync("UNKNOWN", administrator.Username,
            "the wrong password", CancellationToken.None);

        Assert.Null(wrongPassword);
        Assert.Null(unknownAccount);
        Assert.Equal(2, hasher.VerifyCalls);
        Assert.Equal(2, store.DeniedLogins);
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public async Task IssuedTokenIsHashedAndRevocationInvalidatesIt()
    {
        var (company, administrator, store, hasher, clock) = CreateFixture();
        var service = new CentralSessionService(store, hasher, clock);

        var issued = await service.LoginAsync(company.Code, administrator.Username,
            "a unique laboratory password", CancellationToken.None);

        Assert.NotNull(issued);
        Assert.Equal(64, issued.Token.Length);
        Assert.NotEqual(issued.Token, issued.Session.TokenHash);
        Assert.Equal(company.Id, issued.Session.CentralCompanyId);
        Assert.NotNull(await service.AuthenticateAsync(issued.Token, CancellationToken.None));
        Assert.Null(await service.AuthenticateAsync(new string('F', 64), CancellationToken.None));

        await service.LogoutAsync(issued.Token, CancellationToken.None);
        Assert.Null(await service.AuthenticateAsync(issued.Token, CancellationToken.None));
        Assert.Equal(1, store.Revocations);
    }

    [Fact]
    public async Task InactiveCompanyAdministratorOrExpiredSessionNeverAuthorize()
    {
        var (company, administrator, store, hasher, clock) = CreateFixture();
        var service = new CentralSessionService(store, hasher, clock);
        var issued = await service.LoginAsync(company.Code, administrator.Username,
            "a unique laboratory password", CancellationToken.None);
        Assert.NotNull(issued);

        clock.UtcNow = clock.UtcNow.AddHours(9);
        Assert.Null(await service.AuthenticateAsync(issued.Token, CancellationToken.None));

        clock.UtcNow = clock.UtcNow.AddHours(-9);
        administrator.Deactivate();
        Assert.Null(await service.AuthenticateAsync(issued.Token, CancellationToken.None));
        administrator = CentralAdministrator.Create(company.Id, "other-admin",
            hasher.Hash("a unique laboratory password"));
        store.Account = new CentralAccount(company, administrator);
        company.Deactivate();
        Assert.Null(await service.LoginAsync(company.Code, administrator.Username,
            "a unique laboratory password", CancellationToken.None));
    }

    private static (CentralCompany Company, CentralAdministrator Administrator,
        FakeStore Store, CountingHasher Hasher, FixedClock Clock) CreateFixture()
    {
        var company = CentralCompany.Create("VISUAL", "Visual Central");
        var hasher = new CountingHasher();
        var administrator = CentralAdministrator.Create(company.Id, "central-admin",
            hasher.Hash("a unique laboratory password"));
        var store = new FakeStore { Account = new CentralAccount(company, administrator) };
        return (company, administrator, store, hasher,
            new FixedClock { UtcNow = new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero) });
    }

    private sealed class CountingHasher : ICentralPasswordHasher
    {
        private readonly CentralPasswordHasher inner = new();
        public int VerifyCalls { get; private set; }
        public string Hash(string password) => inner.Hash(password);
        public bool Verify(string? password, string? encodedHash)
        {
            VerifyCalls++;
            return inner.Verify(password, encodedHash);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeStore : ICentralSessionStore
    {
        public CentralAccount? Account { get; set; }
        public List<CentralSession> Sessions { get; } = [];
        public int DeniedLogins { get; private set; }
        public int Revocations { get; private set; }

        public Task<CentralAccount?> FindAccountAsync(string companyCode, string usernameNormalized,
            CancellationToken cancellationToken) => Task.FromResult(
            Account?.Company.Code == companyCode
            && Account.Administrator.UsernameNormalized == usernameNormalized ? Account : null);

        public Task RecordDeniedLoginAsync(Guid? companyId, CancellationToken cancellationToken)
        {
            DeniedLogins++;
            return Task.CompletedTask;
        }

        public Task SaveIssuedSessionAsync(CentralSession session, CancellationToken cancellationToken)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<CentralSessionContext?> FindSessionAsync(string tokenHash,
            CancellationToken cancellationToken)
        {
            var session = Sessions.SingleOrDefault(item => item.TokenHash == tokenHash);
            return Task.FromResult(session is null || Account is null ? null
                : new CentralSessionContext(session, Account.Company, Account.Administrator));
        }

        public Task SaveRevokedSessionAsync(CentralSession session, CancellationToken cancellationToken)
        {
            Revocations++;
            return Task.CompletedTask;
        }
    }
}
