using System.Net;
using System.Net.Http.Json;
using Carnicerias.Central.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carnicerias.Central.Tests;

public sealed class CentralManufacturerCompanyEndpointTests
{
    [Fact]
    public async Task OnlyCentralManufacturerCanListCreateAndAddTenantAdministrators()
    {
        using var fixture = new CentralFactory();
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var denied = await client.GetAsync("/api/v1/manufacturer/companies");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var login = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/manufacturer/sessions")
        {
            Content = JsonContent.Create(new
            {
                credential = "superadmin-central", password = "a distinct central password"
            })
        };
        AddOrigin(login);
        using var loggedIn = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var cookie = Assert.Single(loggedIn.Headers.GetValues("Set-Cookie")).Split(';')[0];

        using var listRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/manufacturer/companies?page=1&pageSize=20");
        listRequest.Headers.Add("Cookie", cookie);
        using var listed = await client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Contains("RUSTICA-CENTRAL", await listed.Content.ReadAsStringAsync());

        using var invalidPage = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/manufacturer/companies?pageSize=101");
        invalidPage.Headers.Add("Cookie", cookie);
        using var invalid = await client.SendAsync(invalidPage);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var wrongOrigin = CreateCompanyRequest(cookie);
        wrongOrigin.Headers.Remove("Origin");
        wrongOrigin.Headers.Add("Origin", "https://other.example");
        using var rejected = await client.SendAsync(wrongOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var create = CreateCompanyRequest(cookie);
        using var created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.DoesNotContain("a new tenant password", await created.Content.ReadAsStringAsync());

        using var adminsRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/manufacturer/companies/{fixture.Management.CompanyId}/administrators");
        adminsRequest.Headers.Add("Cookie", cookie);
        using var admins = await client.SendAsync(adminsRequest);
        Assert.Equal(HttpStatusCode.OK, admins.StatusCode);

        using var add = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/manufacturer/companies/{fixture.Management.CompanyId}/administrators")
        {
            Content = JsonContent.Create(new
            {
                username = "another-admin", password = "another tenant password"
            })
        };
        add.Headers.Add("Cookie", cookie);
        AddOrigin(add);
        using var added = await client.SendAsync(add);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.DoesNotContain("another tenant password", await added.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage CreateCompanyRequest(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/manufacturer/companies")
        {
            Content = JsonContent.Create(new
            {
                code = "RUSTICA-2", name = "LA Rústica 2",
                initialAdministratorUsername = "admin-rustica-2",
                initialAdministratorPassword = "a new tenant password"
            })
        };
        request.Headers.Add("Cookie", cookie);
        AddOrigin(request);
        return request;
    }

    private static void AddOrigin(HttpRequestMessage request)
    {
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-Carnicerias-CSRF", "1");
    }

    private sealed class CentralFactory : WebApplicationFactory<Program>
    {
        private readonly TestSessionStore store = new();
        public TestManagement Management { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICentralManufacturerSessionStore>();
                services.AddScoped<ICentralManufacturerSessionStore>(_ => store);
                services.RemoveAll<ICentralCompanyManagementService>();
                services.AddScoped<ICentralCompanyManagementService>(_ => Management);
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

    public sealed class TestManagement : ICentralCompanyManagementService
    {
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Task<CentralManagementPage<CentralManagedCompany>> ListCompaniesAsync(
            int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(
            new CentralManagementPage<CentralManagedCompany>(
                [new(CompanyId, "RUSTICA-CENTRAL", "LA Rústica", true, 1, 0)],
                page, pageSize, 1));

        public Task<CentralManagementPage<CentralManagedAdministrator>?> ListAdministratorsAsync(
            Guid companyId, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<CentralManagementPage<CentralManagedAdministrator>?>(
                companyId == CompanyId
                    ? new([new(Guid.NewGuid(), "admin-rustica", true)], page, pageSize, 1)
                    : null);

        public Task<CentralCreatedCompany> CreateCompanyAsync(string code, string name,
            string username, string password, Guid actorId, CancellationToken cancellationToken) =>
            Task.FromResult(new CentralCreatedCompany(CompanyId, code, name, Guid.NewGuid()));

        public Task<CentralManagedAdministrator?> AddAdministratorAsync(Guid companyId,
            string username, string password, Guid actorId, CancellationToken cancellationToken) =>
            Task.FromResult<CentralManagedAdministrator?>(companyId == CompanyId
                ? new(Guid.NewGuid(), username, true) : null);
    }
}
