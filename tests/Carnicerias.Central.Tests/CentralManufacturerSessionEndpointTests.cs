using System.Net;
using System.Net.Http.Json;
using Carnicerias.Central.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carnicerias.Central.Tests;

public sealed class CentralManufacturerSessionEndpointTests
{
    [Fact]
    public async Task LoginUsesItsOwnCookieAndRejectsTenantSessionAndWrongOrigin()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var tenantCookie = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/manufacturer/sessions/current");
        tenantCookie.Headers.Add("Cookie", "carnicerias.central.session=tenant-token");
        using var tenantResponse = await client.SendAsync(tenantCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, tenantResponse.StatusCode);

        using var wrongOrigin = LoginRequest("https://other.example");
        using var rejected = await client.SendAsync(wrongOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var loginRequest = LoginRequest("https://localhost");
        using var login = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("no-store", login.Headers.CacheControl?.ToString());
        var cookieHeader = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("carnicerias.central.manufacturer=", cookieHeader, StringComparison.Ordinal);
        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/manufacturer", cookieHeader, StringComparison.OrdinalIgnoreCase);
        var cookie = cookieHeader.Split(';')[0];

        using var currentRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/manufacturer/sessions/current");
        currentRequest.Headers.Add("Cookie", cookie);
        using var current = await client.SendAsync(currentRequest);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Contains("superadmin-central", await current.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var tenantRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions/current");
        tenantRequest.Headers.Add("Cookie", cookie);
        using var tenant = await client.SendAsync(tenantRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, tenant.StatusCode);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Delete,
            "/api/v1/manufacturer/sessions/current");
        logoutRequest.Headers.Add("Cookie", cookie);
        logoutRequest.Headers.Add("Origin", "https://localhost");
        logoutRequest.Headers.Add("X-Carnicerias-CSRF", "1");
        using var logout = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    private static HttpRequestMessage LoginRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/manufacturer/sessions")
        {
            Content = JsonContent.Create(new
            {
                credential = "superadmin-central", password = "a distinct central password"
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
                services.RemoveAll<ICentralManufacturerSessionStore>();
                services.AddScoped<ICentralManufacturerSessionStore>(_ => store);
                services.RemoveAll<ICentralSessionStore>();
                services.AddScoped<ICentralSessionStore, NullTenantSessionStore>();
            });
        }
    }

    private sealed class TestSessionStore : ICentralManufacturerSessionStore
    {
        private readonly CentralManufacturer manufacturer = CentralManufacturer.Create(
            "superadmin-central", new CentralPasswordHasher().Hash("a distinct central password"));
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

    private sealed class NullTenantSessionStore : ICentralSessionStore
    {
        public Task<CentralAccount?> FindAccountAsync(string companyCode,
            string usernameNormalized, CancellationToken cancellationToken) =>
            Task.FromResult<CentralAccount?>(null);

        public Task RecordDeniedLoginAsync(Guid? companyId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveIssuedSessionAsync(CentralSession session,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CentralSessionContext?> FindSessionAsync(string tokenHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<CentralSessionContext?>(null);

        public Task SaveRevokedSessionAsync(CentralSession session,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
