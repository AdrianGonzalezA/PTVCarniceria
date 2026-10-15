using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carnicerias.Central.Tests;

public sealed class CentralHealthTests
{
    [Fact]
    public async Task StartsWithoutAccessingTheLocalPosDatabase()
    {
        using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/pos-terminals/current")).StatusCode);
    }
}
