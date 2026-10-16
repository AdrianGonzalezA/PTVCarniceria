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

    [Fact]
    public async Task CheckEditAndStateAreTenantScopedAndDoNotExposeCredential()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });
        using var login = await client.SendAsync(LoginRequest());
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var created = await client.SendAsync(CreateRequest(cookie, true));
        var installationId = Assert.Single(fixture.Store.Installations).InstallationId;

        using var check = await client.SendAsync(ActionRequest(HttpMethod.Post, cookie,
            $"/api/v1/branch-connections/{installationId}/checks"));
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        Assert.Contains("available", await check.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var edit = await client.SendAsync(ActionRequest(HttpMethod.Patch, cookie,
            $"/api/v1/branch-connections/{installationId}", new { label = "Sucursal editada" }));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal("Sucursal editada", Assert.Single(fixture.Store.Installations).Label);
        Assert.DoesNotContain(Token, await edit.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var forbiddenIdChange = await client.SendAsync(ActionRequest(HttpMethod.Patch, cookie,
            $"/api/v1/branch-connections/{installationId}",
            new { label = "No", remoteCompanyId = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.BadRequest, forbiddenIdChange.StatusCode);

        using var off = await client.SendAsync(ActionRequest(HttpMethod.Patch, cookie,
            $"/api/v1/branch-connections/{installationId}/state", new { isActive = false }));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False(Assert.Single(fixture.Store.Installations).IsActive);

        var other = CentralInstallation.Create(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "Other", "https://branch.example/", "OTHER_TOKEN");
        fixture.Store.Installations.Add(other);
        using var scopedList = await client.SendAsync(GetRequest(cookie));
        Assert.DoesNotContain("Other", await scopedList.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        using var otherCheck = await client.SendAsync(ActionRequest(HttpMethod.Post, cookie,
            $"/api/v1/branch-connections/{other.InstallationId}/checks"));
        Assert.Equal(HttpStatusCode.NotFound, otherCheck.StatusCode);
        using var otherEdit = await client.SendAsync(ActionRequest(HttpMethod.Patch, cookie,
            $"/api/v1/branch-connections/{other.InstallationId}", new { label = "Hijack" }));
        Assert.Equal(HttpStatusCode.NotFound, otherEdit.StatusCode);
        using var otherState = await client.SendAsync(ActionRequest(HttpMethod.Patch, cookie,
            $"/api/v1/branch-connections/{other.InstallationId}/state", new { isActive = false }));
        Assert.Equal(HttpStatusCode.NotFound, otherState.StatusCode);
        Assert.True(other.IsActive);

        using var missingOrigin = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/branch-connections/{installationId}/checks");
        missingOrigin.Headers.Add("Cookie", cookie);
        using var rejectedOrigin = await client.SendAsync(missingOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, rejectedOrigin.StatusCode);

        using var unauthenticated = await client.SendAsync(ActionRequest(HttpMethod.Patch,
            "carnicerias.central.session=invalid",
            $"/api/v1/branch-connections/{installationId}/state", new { isActive = true }));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
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

    private static HttpRequestMessage ActionRequest(HttpMethod method, string cookie,
        string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-Carnicerias-CSRF", "1");
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
                services.RemoveAll<ICentralBranchCredentialResolver>();
                services.AddScoped<ICentralBranchCredentialResolver>(_ =>
                    new FixtureCredentialResolver(Store, key));
            });
        }
    }

    private sealed class FixtureStore : ICentralSessionStore, ICentralBranchConnectionStore
    {
        public CentralCompany Company { get; } = CentralCompany.Create("A-COMPANY", "A");
        private readonly CentralAdministrator administrator;
        private readonly List<CentralSession> sessions = [];
        public List<CentralInstallation> Installations { get; } = [];
        public List<CentralBranchCredential> Credentials { get; } = [];

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
                item.IsActive, true, item.LastCheckStatus, item.LastCheckedAtUtc)).ToArray();
            return Task.FromResult(new CentralManagementPage<CentralBranchConnectionView>(
                data, page, pageSize, scope.Length));
        }

        public Task SaveRegisteredAsync(CentralInstallation installation,
            CentralBranchCredential credential, CentralAuditEvent audit,
            CancellationToken cancellationToken)
        {
            Installations.Add(installation);
            Credentials.Add(credential);
            return Task.CompletedTask;
        }
        public Task<CentralBranchConnectionSnapshot?> FindAsync(Guid companyId, Guid installationId,
            CancellationToken cancellationToken)
        {
            var installation = Installations.SingleOrDefault(item =>
                item.CentralCompanyId == companyId && item.InstallationId == installationId);
            return Task.FromResult(installation is null ? null :
                new CentralBranchConnectionSnapshot(installation,
                    Credentials.Any(item => item.InstallationId == installationId)));
        }
        public Task SaveChangedAsync(CentralInstallation installation,
            CentralBranchCredential? credential, CentralAuditEvent audit,
            long expectedRevision, CancellationToken cancellationToken)
        {
            if (credential is not null)
            {
                Credentials.RemoveAll(item => item.InstallationId == installation.InstallationId);
                Credentials.Add(credential);
            }
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

    private sealed class FixtureCredentialResolver(FixtureStore store, byte[] key)
        : ICentralBranchCredentialResolver
    {
        public Task<string> ResolveAsync(CentralInstallation installation,
            CancellationToken cancellationToken)
        {
            var credential = store.Credentials.Single(item =>
                item.InstallationId == installation.InstallationId);
            var crypto = new CentralBranchCredentialCrypto(1, version =>
                version == 1 ? key : null);
            return Task.FromResult(crypto.Decrypt(installation.CentralCompanyId,
                installation.InstallationId, credential.ToEnvelope()));
        }
    }
}
