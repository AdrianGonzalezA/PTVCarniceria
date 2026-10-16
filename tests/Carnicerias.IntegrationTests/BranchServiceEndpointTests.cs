using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Carnicerias.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Carnicerias.IntegrationTests;

public sealed class BranchServiceEndpointTests
{
    private const string Token = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string NextToken = "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f";

    [Fact]
    public async Task DoesNotExposeRoutesByDefaultOrWithIncompleteConfiguration()
    {
        using var disabled = CreateApplication([]);
        using var disabledClient = disabled.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await disabledClient.GetAsync("/api/central/v1/installation")).StatusCode);

        using var incomplete = CreateApplication(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "true"
        });
        using var incompleteClient = incomplete.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await incompleteClient.GetAsync("/api/central/v1/installation")).StatusCode);
    }

    [Fact]
    public async Task RejectsMissingOrInvalidServiceCredentialWithoutReadingDatabase()
    {
        using var application = CreateApplication(EnabledConfiguration());
        using var client = application.CreateClient();

        using var missing = await client.GetAsync("/api/central/v1/installation");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("no-store", missing.Headers.CacheControl?.ToString());
        Assert.Equal("NOT_AUTHENTICATED",
            (await missing.Content.ReadFromJsonAsync<ErrorResponse>())?.Error.Code);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('f', 64));
        using var invalid = await client.GetAsync("/api/central/v1/installation");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", Token);
        client.DefaultRequestHeaders.Add("Cookie", "carnicerias_session=not-a-service-credential");
        using var otherCredentialKinds = await client.GetAsync("/api/central/v1/installation");
        Assert.Equal(HttpStatusCode.Unauthorized, otherCredentialKinds.StatusCode);
    }

    [Fact]
    public async Task RejectsExtraParametersAndDoesNotAuthorizeOperationalRoutes()
    {
        using var application = CreateApplication(EnabledConfiguration());
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using var extra = await client.GetAsync("/api/central/v1/installation?branchId=other");
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            (await extra.Content.ReadFromJsonAsync<ErrorResponse>())?.Error.Code);

        using var admin = await client.GetAsync("/api/admin/history/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
        using var terminal = await client.GetAsync("/api/pos-terminals/current");
        Assert.Equal(HttpStatusCode.Unauthorized, terminal.StatusCode);
    }

    [Theory]
    [InlineData("/api/central/v1/summary")]
    [InlineData("/api/central/v1/summary?fromUtc=2026-10-01T00:00:00Z&toUtc=2026-11-02T00:00:00Z")]
    [InlineData("/api/central/v1/summary?fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-02T00:00:00Z&branchId=other")]
    [InlineData("/api/central/v1/summary?fromUtc=2026-10-01T00:00:00-03:00&toUtc=2026-10-02T00:00:00Z")]
    public async Task RejectsInvalidSummaryWindowOrScopeSelection(string url)
    {
        using var application = CreateApplication(EnabledConfiguration());
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("VALIDATION_ERROR",
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Error.Code);
    }

    [Fact]
    public async Task ValidSummaryRequestWithoutAvailableBindingReturnsSanitizedServiceError()
    {
        var config = EnabledConfiguration();
        config.Remove("ConnectionStrings:PlatformAccess");
        using var application = CreateApplication(config);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using var response = await client.GetAsync(
            "/api/central/v1/summary?fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-02T00:00:00Z");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("BRANCH_BINDING_UNAVAILABLE",
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Error.Code);
    }

    [Fact]
    public async Task ReloadsCredentialForRotationWithoutChangingThePinnedBranch()
    {
        using var application = CreateApplication(EnabledConfiguration());
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NextToken);
        const string url = "/api/central/v1/installation?unexpected=1";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);

        var configuration = application.Services.GetRequiredService<IConfiguration>();
        configuration["Federation:NextCredentialSha256"] =
            Convert.ToHexString(SHA256.HashData(Convert.FromHexString(NextToken)));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);

        configuration["Federation:BranchId"] = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task LimitsCentralRequestsWithoutAffectingOtherRoutes()
    {
        using var application = CreateApplication(EnabledConfiguration());
        using var client = application.CreateClient();
        for (var request = 0; request < 30; request++)
        {
            using var denied = await client.GetAsync("/api/central/v1/installation");
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        using var limited = await client.GetAsync("/api/central/v1/installation");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
        Assert.Equal("RATE_LIMITED",
            (await limited.Content.ReadFromJsonAsync<ErrorResponse>())?.Error.Code);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    private static WebApplicationFactory<Program> CreateApplication(Dictionary<string, string?> values) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(values)));

    private static Dictionary<string, string?> EnabledConfiguration() => new()
    {
        ["Federation:Enabled"] = "true",
        ["Federation:InstallationId"] = Guid.NewGuid().ToString(),
        ["Federation:CompanyId"] = Guid.NewGuid().ToString(),
        ["Federation:BranchId"] = Guid.NewGuid().ToString(),
        ["Federation:CredentialSha256"] = Convert.ToHexString(SHA256.HashData(Convert.FromHexString(Token))),
        ["ConnectionStrings:PlatformAccess"] =
            "Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres"
    };
}
