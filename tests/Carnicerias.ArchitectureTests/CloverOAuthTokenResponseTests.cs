using Carnicerias.Api.Payments;

namespace Carnicerias.ArchitectureTests;

public sealed class CloverOAuthTokenResponseTests
{
    [Fact]
    public void AcceptsExpiringAccessAndRefreshPair()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var json = """{"access_token":"access","refresh_token":"refresh","access_token_expiration":1800003600,"refresh_token_expiration":1800086400}""";

        var result = CloverOAuthTokenResponse.Parse(json, now);

        Assert.NotNull(result);
        Assert.Equal("access", result.AccessToken);
        Assert.Equal(now.AddDays(1), result.RefreshExpiresAtUtc);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"x\",\"refresh_token\":\"y\",\"access_token_expiration\":0,\"refresh_token_expiration\":1800086400}")]
    [InlineData("{\"access_token\":\"x\",\"refresh_token\":\"y\",\"access_token_expiration\":1800086400,\"refresh_token_expiration\":1800003600}")]
    public void RejectsMissingOrInvalidTokenPair(string json)
    {
        Assert.Null(CloverOAuthTokenResponse.Parse(json, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000)));
    }
}
