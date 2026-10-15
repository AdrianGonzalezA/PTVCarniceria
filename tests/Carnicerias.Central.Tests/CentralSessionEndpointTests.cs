using System.Net;
using System.Net.Http.Json;
using Carnicerias.Central.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carnicerias.Central.Tests;

public sealed class CentralSessionEndpointTests
{
    [Fact]
    public async Task LoginCurrentAndLogoutUseOnlyTheCentralSecureCookie()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var localRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions/current");
        localRequest.Headers.Add("Cookie", "carnicerias.session=local-token");
        using var localResponse = await client.SendAsync(localRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, localResponse.StatusCode);
        Assert.Equal("no-store", localResponse.Headers.CacheControl?.ToString());

        using var wrongOrigin = LoginRequest("https://other.example");
        using var rejected = await client.SendAsync(wrongOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var loginRequest = LoginRequest("https://localhost");
        using var login = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var setCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("carnicerias.central.session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        var cookie = setCookie.Split(';')[0];

        using var currentRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions/current");
        currentRequest.Headers.Add("Cookie", cookie);
        using var current = await client.SendAsync(currentRequest);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Contains("central-admin", await current.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var unsafeLogout = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/sessions/current");
        unsafeLogout.Headers.Add("Cookie", cookie);
        using var unsafeResponse = await client.SendAsync(unsafeLogout);
        Assert.Equal(HttpStatusCode.Forbidden, unsafeResponse.StatusCode);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/sessions/current");
        logoutRequest.Headers.Add("Cookie", cookie);
        logoutRequest.Headers.Add("Origin", "https://localhost");
        logoutRequest.Headers.Add("X-Carnicerias-CSRF", "1");
        using var logout = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var oldCookieRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions/current");
        oldCookieRequest.Headers.Add("Cookie", cookie);
        using var oldCookieResponse = await client.SendAsync(oldCookieRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, oldCookieResponse.StatusCode);
    }

    [Fact]
    public async Task InvalidShapeAndExcessiveLoginAttemptsHaveStructuredErrors()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sessions")
        {
            Content = JsonContent.Create(new { companyCode = "", credential = "", password = "" })
        };
        malformed.Headers.Add("Origin", "https://localhost");
        malformed.Headers.Add("X-Carnicerias-CSRF", "1");
        using var invalid = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("error", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var request = LoginRequest("https://localhost", "wrong password here");
            last?.Dispose();
            last = await client.SendAsync(request);
        }

        Assert.NotNull(last);
        using (last)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
            Assert.Contains("error", await last.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ShortButNonemptyPasswordReachesAuthenticationInsteadOfShapeRejection()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var request = LoginRequest("https://localhost", "short1");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage LoginRequest(string origin,
        string password = "a unique laboratory password")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sessions")
        {
            Content = JsonContent.Create(new
            {
                companyCode = "VISUAL", credential = "central-admin", password
            })
        };
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Carnicerias-CSRF", "1");
        return request;
    }

    private sealed class CentralFactory : WebApplicationFactory<Program>
    {
        private readonly TestSessionStore store = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICentralSessionStore>();
                services.AddScoped<ICentralSessionStore>(_ => store);
            });
        }
    }

    private sealed class TestSessionStore : ICentralSessionStore
    {
        private readonly CentralCompany company = CentralCompany.Create("VISUAL", "Visual Central");
        private readonly List<CentralSession> sessions = [];
        private CentralAdministrator? administrator;

        public Task<CentralAccount?> FindAccountAsync(string companyCode, string usernameNormalized,
            CancellationToken cancellationToken)
        {
            administrator ??= CentralAdministrator.Create(company.Id, "central-admin",
                new CentralPasswordHasher().Hash("a unique laboratory password"));
            return Task.FromResult(companyCode == company.Code
                && usernameNormalized == administrator.UsernameNormalized
                ? new CentralAccount(company, administrator) : null);
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
            return Task.FromResult(session is null || administrator is null ? null
                : new CentralSessionContext(session, company, administrator));
        }

        public Task SaveRevokedSessionAsync(CentralSession session, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
