using System.Net;
using System.Net.Http.Json;
using Carnicerias.Central.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carnicerias.Central.Tests;

public sealed class CentralBusinessSummaryEndpointTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 15, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SessionTenantSelectsBranchesAndDefaultUsesArgentinaToday()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var anonymous = await client.GetAsync("/api/v1/business/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var login = await client.SendAsync(LoginRequest("A-COMPANY"));
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var response = await client.SendAsync(SummaryRequest(cookie));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = await System.Text.Json.JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Equal("2026-10-14T03:00:00+00:00",
            root.GetProperty("window").GetProperty("fromUtc").GetString());
        Assert.Equal(Now, root.GetProperty("window").GetProperty("toUtc").GetDateTimeOffset());
        Assert.Equal("complete", root.GetProperty("completeness").GetString());
        Assert.Equal("Sucursal A", root.GetProperty("branches")[0].GetProperty("label").GetString());
        Assert.Equal(fixture.AId, fixture.Reader.RequestedCompanyId);
        var raw = root.GetRawText();
        Assert.DoesNotContain("Sucursal B", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("branch.example", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("BRANCH_TOKEN", raw, StringComparison.Ordinal);

        using var loginB = await client.SendAsync(LoginRequest("B-COMPANY"));
        var cookieB = Assert.Single(loginB.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var responseB = await client.SendAsync(SummaryRequest(cookieB));
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        Assert.Equal(fixture.BId, fixture.Reader.RequestedCompanyId);
        Assert.Contains("Sucursal B", await responseB.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("Sucursal A", await responseB.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitWindowRejectsMissingInvalidOrTenantSelectors()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });
        using var login = await client.SendAsync(LoginRequest("A-COMPANY"));
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];

        foreach (var query in new[]
        {
            "?fromUtc=2026-10-14T03%3A00%3A00Z",
            "?companyId=" + fixture.BId,
            "?fromUtc=2026-10-14T03%3A00%3A00Z&toUtc=2026-10-14T03%3A00%3A00Z",
            "?fromUtc=2026-10-01T00%3A00%3A00Z&toUtc=2026-11-02T00%3A00%3A00Z"
        })
        {
            using var invalid = await client.SendAsync(
                SummaryRequest(cookie, "/api/v1/business/summary" + query));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(0, fixture.Reader.ReadCount);

        using var valid = await client.SendAsync(SummaryRequest(cookie,
            "/api/v1/business/summary?fromUtc=2026-10-13T03%3A00%3A00Z&toUtc=2026-10-14T03%3A00%3A00Z"));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var localDates = await client.SendAsync(SummaryRequest(cookie,
            "/api/v1/business/summary?fromDate=2026-10-14&toDate=2026-10-15"));
        Assert.Equal(HttpStatusCode.OK, localDates.StatusCode);
        Assert.Equal(2, fixture.Reader.ReadCount);
    }

    [Fact]
    public async Task LiveFanoutIsRateLimitedPerClientAddress()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });
        using var login = await client.SendAsync(LoginRequest("A-COMPANY"));
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];

        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var permitted = await client.SendAsync(SummaryRequest(cookie));
            Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
        }

        using var rejected = await client.SendAsync(SummaryRequest(cookie));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("QUERY_RATE_LIMITED", await rejected.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
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

    private static HttpRequestMessage SummaryRequest(string cookie,
        string path = "/api/v1/business/summary")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private sealed class CentralFactory : WebApplicationFactory<Program>
    {
        private readonly FixtureStore store = new();
        public FixtureReader Reader { get; }
        public Guid AId => store.A.Id;
        public Guid BId => store.B.Id;

        public CentralFactory() => Reader = new FixtureReader(store.A.Id);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICentralSessionStore>();
                services.AddScoped<ICentralSessionStore>(_ => store);
                services.RemoveAll<ICentralBusinessInstallationReader>();
                services.AddScoped<ICentralBusinessInstallationReader>(_ => Reader);
                services.RemoveAll<ICentralBranchSummarySource>();
                services.AddScoped<ICentralBranchSummarySource>(_ => new FixtureSource());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider());
            });
        }
    }

    private sealed class FixtureReader(Guid companyAId) : ICentralBusinessInstallationReader
    {
        public Guid RequestedCompanyId { get; private set; }
        public int ReadCount { get; private set; }
        public Task<IReadOnlyList<CentralInstallation>> ReadActiveAsync(Guid centralCompanyId,
            CancellationToken cancellationToken)
        {
            RequestedCompanyId = centralCompanyId;
            ReadCount++;
            return Task.FromResult<IReadOnlyList<CentralInstallation>>(
                [CentralInstallation.Create(Guid.NewGuid(), centralCompanyId, Guid.NewGuid(),
                    Guid.NewGuid(), centralCompanyId == companyAId ? "Sucursal A" : "Sucursal B",
                    "https://branch.example/", "BRANCH_TOKEN")]);
        }
    }

    private sealed class FixtureSource : ICentralBranchSummarySource
    {
        public Task<CentralBranchSummary> ReadAsync(CentralInstallation installation,
            CentralBusinessWindow window, CancellationToken cancellationToken) =>
            Task.FromResult(new CentralBranchSummary(Now, 1, 100m, 100m,
                [new CentralPaymentSummary("cash", 100m)], 1, 30m));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixtureStore : ICentralSessionStore
    {
        public CentralCompany A { get; } = CentralCompany.Create("A-COMPANY", "A");
        public CentralCompany B { get; } = CentralCompany.Create("B-COMPANY", "B");
        private readonly Dictionary<Guid, CentralAdministrator> administrators = [];
        private readonly List<CentralSession> sessions = [];

        public FixtureStore()
        {
            var hash = new CentralPasswordHasher().Hash("a unique laboratory password");
            administrators[A.Id] = CentralAdministrator.Create(A.Id, "central-admin", hash);
            administrators[B.Id] = CentralAdministrator.Create(B.Id, "central-admin", hash);
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
    }
}
