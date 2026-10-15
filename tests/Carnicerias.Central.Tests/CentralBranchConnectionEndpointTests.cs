using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Carnicerias.Central.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carnicerias.Central.Tests;

public sealed class CentralBranchConnectionEndpointTests
{
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task ManagementRoutesRequireTenantSessionAndOriginThenNeverReturnToken()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var anonymous = await client.GetAsync("/api/v1/branch-connections");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var login = await client.SendAsync(LoginRequest());
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var missingCsrf = await client.SendAsync(CreateRequest(cookie, false));
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        Assert.Empty(fixture.Store.Installations);

        using var created = await client.SendAsync(CreateRequest(cookie, true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token, createdBody, StringComparison.Ordinal);
        Assert.DoesNotContain("secretReference", createdBody, StringComparison.OrdinalIgnoreCase);

        using var list = await client.SendAsync(GetRequest(cookie));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Contains("Sucursal", listBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, listBody, StringComparison.Ordinal);
        Assert.Equal(fixture.Store.Company.Id, Assert.Single(fixture.Store.Installations).CentralCompanyId);
    }

    private static HttpRequestMessage LoginRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sessions")
        {
            Content = JsonContent.Create(new
            {
                companyCode = "A-COMPANY", credential = "central-admin",
                password = "a unique laboratory password"
            })
        };
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-Carnicerias-CSRF", "1");
        return request;
    }

    private static HttpRequestMessage GetRequest(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/branch-connections?page=1&pageSize=20");
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static HttpRequestMessage CreateRequest(string cookie, bool csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/branch-connections")
        {
            Content = JsonContent.Create(new
            {
                installationId = Guid.NewGuid(), remoteCompanyId = Guid.NewGuid(),
                remoteBranchId = Guid.NewGuid(), label = "Sucursal",
                destinationUrl = "https://branch.example/", token = Token
            })
        };
        request.Headers.Add("Cookie", cookie);
        if (csrf)
        {
            request.Headers.Add("Origin", "https://localhost");
            request.Headers.Add("X-Carnicerias-CSRF", "1");
        }
        return request;
    }

    private sealed class CentralFactory : WebApplicationFactory<Program>
    {
        public FixtureStore Store { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICentralSessionStore>();
                services.AddScoped<ICentralSessionStore>(_ => Store);
                services.RemoveAll<ICentralBranchConnectionStore>();
                services.AddScoped<ICentralBranchConnectionStore>(_ => Store);
                services.RemoveAll<ICentralInstallationVerifier>();
                services.AddSingleton<ICentralInstallationVerifier, AcceptVerifier>();
                services.RemoveAll<CentralDestinationPolicy>();
                services.AddSingleton(new CentralDestinationPolicy(
                    [new CentralApprovedEndpoint(new Uri("https://branch.example/"),
                        [IPAddress.Parse("10.10.1.25")], Store.Company.Id)],
                    false, new FakeDns()));
                services.RemoveAll<Func<CentralBranchCredentialCrypto>>();
                var key = RandomNumberGenerator.GetBytes(32);
                services.AddSingleton<Func<CentralBranchCredentialCrypto>>(
                    _ => () => new CentralBranchCredentialCrypto(1,
                        version => version == 1 ? key : null));
            });
        }
    }

    private sealed class FixtureStore : ICentralSessionStore, ICentralBranchConnectionStore
    {
        public CentralCompany Company { get; } = CentralCompany.Create("A-COMPANY", "A");
        private readonly CentralAdministrator administrator;
        private readonly List<CentralSession> sessions = [];
        public List<CentralInstallation> Installations { get; } = [];

        public FixtureStore() => administrator = CentralAdministrator.Create(Company.Id,
            "central-admin", new CentralPasswordHasher().Hash("a unique laboratory password"));

        public Task<CentralAccount?> FindAccountAsync(string companyCode, string usernameNormalized,
            CancellationToken cancellationToken) => Task.FromResult<CentralAccount?>(
            companyCode == Company.Code && usernameNormalized == administrator.UsernameNormalized
                ? new CentralAccount(Company, administrator) : null);
        public Task RecordDeniedLoginAsync(Guid? companyId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task SaveIssuedSessionAsync(CentralSession session, CancellationToken cancellationToken)
        {
            sessions.Add(session);
            return Task.CompletedTask;
        }
        public Task<CentralSessionContext?> FindSessionAsync(string tokenHash,
            CancellationToken cancellationToken)
        {
            var session = sessions.SingleOrDefault(item => item.TokenHash == tokenHash);
            return Task.FromResult<CentralSessionContext?>(session is null ? null
                : new CentralSessionContext(session, Company, administrator));
        }
        public Task SaveRevokedSessionAsync(CentralSession session,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CentralManagementPage<CentralBranchConnectionView>> ListAsync(Guid companyId,
            int page, int pageSize, CancellationToken cancellationToken)
        {
            var scope = Installations.Where(item => item.CentralCompanyId == companyId).ToArray();
            var data = scope.Select(item => new CentralBranchConnectionView(item.InstallationId,
                item.RemoteCompanyId, item.RemoteBranchId, item.Label, item.DestinationUrl,
                item.IsActive, true)).ToArray();
            return Task.FromResult(new CentralManagementPage<CentralBranchConnectionView>(
                data, page, pageSize, scope.Length));
        }

        public Task SaveRegisteredAsync(CentralInstallation installation,
            CentralBranchCredential credential, CentralAuditEvent audit,
            CancellationToken cancellationToken)
        {
            Installations.Add(installation);
            return Task.CompletedTask;
        }
        public Task RecordRejectedAsync(CentralAuditEvent audit,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeDns : ICentralDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { IPAddress.Parse("10.10.1.25") });
    }

    private sealed class AcceptVerifier : ICentralInstallationVerifier
    {
        public Task VerifyAsync(CentralApprovedDestination destination, string token,
            Guid installationId, Guid companyId, Guid branchId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
