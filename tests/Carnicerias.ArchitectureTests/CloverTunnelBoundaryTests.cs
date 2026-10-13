using Carnicerias.Api.Payments;

namespace Carnicerias.ArchitectureTests;

public sealed class CloverTunnelBoundaryTests
{
    [Theory]
    [InlineData("GET", "/clover")]
    [InlineData("GET", "/clover/launch")]
    [InlineData("GET", "/clover/callback")]
    [InlineData("GET", "/clover/status")]
    public void AllowsOnlyPublicCloverGetRoutes(string method, string path)
    {
        Assert.True(CloverTunnelBoundary.Allows(method, path));
    }

    [Theory]
    [InlineData("GET", "/api/health")]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/clover/callback")]
    [InlineData("GET", "/clover/callback/extra")]
    [InlineData("GET", "/clover%2f..%2fapi/health")]
    public void BlocksEverythingElse(string method, string path)
    {
        Assert.False(CloverTunnelBoundary.Allows(method, path));
    }
}
