using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralManufacturerSessionServiceTests
{
    [Fact]
    public async Task IssuesRevokesAndSeparatesManufacturerSessionFromTenantLogin()
    {
        var hasher = new CentralPasswordHasher();
        var manufacturer = CentralManufacturer.Create("superadmin-central",
            hasher.Hash("a distinct central password"));
        var store = new TestStore(manufacturer);
        var service = new CentralManufacturerSessionService(store, hasher, TimeProvider.System);

        Assert.Null(await service.LoginAsync("unknown", "a distinct central password", default));
        Assert.Null(await service.LoginAsync(manufacturer.Username, "incorrect password", default));
        var issued = await service.LoginAsync(manufacturer.Username,
            "a distinct central password", default);

        Assert.NotNull(issued);
        Assert.Equal(64, issued.Token.Length);
        Assert.NotNull(await service.AuthenticateAsync(issued.Token, default));
        Assert.Null(await service.AuthenticateAsync("carnicerias.central.session=tenant", default));
        Assert.True(await service.LogoutAsync(issued.Token, default));
        Assert.Null(await service.AuthenticateAsync(issued.Token, default));
    }

    private sealed class TestStore(CentralManufacturer manufacturer) : ICentralManufacturerSessionStore
    {
        private readonly List<CentralManufacturerSession> sessions = [];

        public Task<CentralManufacturer?> FindAccountAsync(string usernameNormalized,
            CancellationToken cancellationToken) => Task.FromResult<CentralManufacturer?>(
            usernameNormalized == manufacturer.UsernameNormalized ? manufacturer : null);

        public Task RecordDeniedLoginAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveIssuedSessionAsync(CentralManufacturerSession session,
            CancellationToken cancellationToken)
        {
            sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<CentralManufacturerSessionContext?> FindSessionAsync(string tokenHash,
            CancellationToken cancellationToken)
        {
            var session = sessions.SingleOrDefault(item => item.TokenHash == tokenHash);
            return Task.FromResult<CentralManufacturerSessionContext?>(session is null ? null
                : new CentralManufacturerSessionContext(session, manufacturer));
        }

        public Task SaveRevokedSessionAsync(CentralManufacturerSession session,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
