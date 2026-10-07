using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carnicerias.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public HealthEndpointTests(WebApplicationFactory<Program> application)
    {
        client = application.CreateClient();
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
