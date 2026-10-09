using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Carnicerias.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public HealthEndpointTests(WebApplicationFactory<Program> application)
    {
        client = application.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PlatformAccess"] =
                    "Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres"
            }))).CreateClient();
    }

    [Fact]
    public async Task GetHealthReturnsHealthyStatus()
    {
        var response = await client.GetAsync("/api/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(CancellationToken.None);
        Assert.Equal("healthy", body?.Status);
    }

    private sealed record HealthResponse(string Status);
}
