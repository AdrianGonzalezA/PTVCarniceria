using Carnicerias.Api.Payments;

namespace Carnicerias.ArchitectureTests;

public sealed class CloverOAuthFlowTests
{
    [Fact]
    public void StateIsOpaqueAndHashIsStable()
    {
        var state = CloverOAuthFlow.NewState();

        Assert.InRange(state.Length, 40, 80);
        Assert.Matches("^[A-Za-z0-9_-]+$", state);
        Assert.Equal(CloverOAuthFlow.HashState(state), CloverOAuthFlow.HashState(state));
        Assert.NotEqual(state, CloverOAuthFlow.HashState(state));
        Assert.NotEqual(CloverOAuthFlow.HashState(state), CloverOAuthFlow.HashState(CloverOAuthFlow.NewState()));
    }

    [Fact]
    public void AuthorizeUrlUsesOnlyFixedSandboxHostAndCallback()
    {
        var url = CloverOAuthFlow.AuthorizeUrl("APP_123", "opaque-state");
        var uri = new Uri(url);

        Assert.Equal("sandbox.dev.clover.com", uri.Host);
        Assert.Equal("/oauth/v2/authorize", uri.AbsolutePath);
        Assert.Contains("redirect_uri=https%3A%2F%2Fclover-sandbox.nuestromundo.ar%2Fclover%2Fcallback", url);
        Assert.Contains("state=opaque-state", url);
    }

    [Theory]
    [InlineData("", "code", "merchant")]
    [InlineData("bad!", "code", "merchant")]
    [InlineData("valid_state", "", "merchant")]
    [InlineData("valid_state", "code", "../other")]
    public void InvalidCallbackParametersAreRejected(string state, string code, string merchantId)
    {
        Assert.False(CloverOAuthFlow.ValidCallback(state, code, merchantId));
    }
}
