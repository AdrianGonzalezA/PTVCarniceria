using System.Net;
using System.Net.Http.Json;
using Carnicerias.Central.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carnicerias.Central.Tests;

public sealed class CentralInstallationEndpointTests
{
    [Fact]
    public void QueryScopeExcludesOtherCompaniesAndInactiveInstallations()
    {
        var a = CentralCompany.Create("A-COMPANY", "A");
        var b = CentralCompany.Create("B-COMPANY", "B");
        var activeA = Installation(a.Id, "Activa A");
        var inactiveA = Installation(a.Id, "Inactiva A");
        inactiveA.Deactivate();
        var activeB = Installation(b.Id, "Activa B");

        var result = CentralInstallationQuery.Scope(
            new[] { activeB, inactiveA, activeA }.AsQueryable(), a.Id).ToArray();

        Assert.Equal(activeA.InstallationId, Assert.Single(result).InstallationId);
    }

    [Fact]
    public async Task EndpointUsesSessionTenantAndNeverReturnsConnectionSecrets()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var anonymous = await client.GetAsync("/api/v1/installations");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var loginA = await client.SendAsync(LoginRequest("A-COMPANY"));
        var cookieA = Assert.Single(loginA.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var listA = await client.SendAsync(ListRequest(cookieA));
        Assert.Equal(HttpStatusCode.OK, listA.StatusCode);
        Assert.Equal("no-store", listA.Headers.CacheControl?.ToString());
        var jsonA = await listA.Content.ReadAsStringAsync();
        Assert.Contains("Activa A", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("Activa B", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("branch.internal.example", jsonA, StringComparison.Ordinal);
        Assert.DoesNotContain("BRANCH_TOKEN", jsonA, StringComparison.Ordinal);
        Assert.Contains("totalCount", jsonA, StringComparison.Ordinal);

        using var invalid = await client.SendAsync(
            ListRequest(cookieA, "/api/v1/installations?companyId=" + fixture.BId));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var invalidPage = await client.SendAsync(
            ListRequest(cookieA, "/api/v1/installations?page=0&pageSize=101"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);

        using var loginB = await client.SendAsync(LoginRequest("B-COMPANY"));
        var cookieB = Assert.Single(loginB.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var listB = await client.SendAsync(ListRequest(cookieB));
        var jsonB = await listB.Content.ReadAsStringAsync();
        Assert.Contains("Activa B", jsonB, StringComparison.Ordinal);
        Assert.DoesNotContain("Activa A", jsonB, StringComparison.Ordinal);
    }

    private static HttpRequestMessage LoginRequest(string companyCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sessions")
        {
            Content = JsonContent.Create(new
            {
                companyCode, credential = "central-admin",
                password = "a unique laboratory password"
            })
        };
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-Carnicerias-CSRF", "1");
        return request;
    }

    private static HttpRequestMessage ListRequest(string cookie,
        string path = "/api/v1/installations?page=1&pageSize=20")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static CentralInstallation Installation(Guid centralCompanyId, string label) =>
        CentralInstallation.Create(Guid.NewGuid(), centralCompanyId,
            Guid.NewGuid(), Guid.NewGuid(), label,
            "https://branch.internal.example/", "BRANCH_TOKEN");

    private sealed class CentralFactory : WebApplicationFactory<Program>
    {
        private readonly FixtureStore store = new();
        public Guid BId => store.B.Id;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICentralSessionStore>();
                services.AddScoped<ICentralSessionStore>(_ => store);
                services.RemoveAll<ICentralInstallationReader>();
                services.AddScoped<ICentralInstallationReader>(_ => store);
            });
        }
    }

    private sealed class FixtureStore : ICentralSessionStore, ICentralInstallationReader
    {
        public CentralCompany A { get; } = CentralCompany.Create("A-COMPANY", "A");
        public CentralCompany B { get; } = CentralCompany.Create("B-COMPANY", "B");
        private readonly Dictionary<Guid, CentralAdministrator> administrators = [];
        private readonly List<CentralSession> sessions = [];
        private readonly List<CentralInstallation> installations;

        public FixtureStore()
        {
            var hash = new CentralPasswordHasher().Hash("a unique laboratory password");
            administrators[A.Id] = CentralAdministrator.Create(A.Id, "central-admin", hash);
            administrators[B.Id] = CentralAdministrator.Create(B.Id, "central-admin", hash);
            installations = [Installation(A.Id, "Activa A"), Installation(B.Id, "Activa B")];
        }

        public Task<CentralAccount?> FindAccountAsync(string companyCode, string usernameNormalized,
            CancellationToken cancellationToken)
        {
            var company = new[] { A, B }.SingleOrDefault(item => item.Code == companyCode);
            return Task.FromResult(company is not null
                && administrators[company.Id].UsernameNormalized == usernameNormalized
                ? new CentralAccount(company, administrators[company.Id]) : null);
        }

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
            if (session is null) return Task.FromResult<CentralSessionContext?>(null);
            var company = session.CentralCompanyId == A.Id ? A : B;
            return Task.FromResult<CentralSessionContext?>(new CentralSessionContext(
                session, company, administrators[company.Id]));
        }

        public Task SaveRevokedSessionAsync(CentralSession session, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<CentralInstallationPage> ReadAsync(Guid centralCompanyId, int page,
            int pageSize, CancellationToken cancellationToken)
        {
            var scope = CentralInstallationQuery.Scope(installations.AsQueryable(), centralCompanyId);
            var total = scope.Count();
            var data = scope.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(item => new CentralInstallationListItem(item.InstallationId,
                    item.RemoteCompanyId, item.RemoteBranchId, item.Label)).ToArray();
            return Task.FromResult(new CentralInstallationPage(data, page, pageSize, total));
        }
    }
}
