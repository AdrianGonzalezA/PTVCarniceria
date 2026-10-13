using System.Net;
using Carnicerias.Api.Payments;

namespace Carnicerias.ArchitectureTests;

public sealed class CloverOAuthClientTests
{
    [Fact]
    public async Task ExchangesOnlyAgainstOfficialSandboxTokenEndpoint()
    {
        Uri? requestedUrl = null;
        string? sentBody = null;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            requestedUrl = request.RequestUri;
            sentBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"access","refresh_token":"refresh","access_token_expiration":1800003600,"refresh_token_expiration":1800086400}"""),
            };
        }));

        var result = await new CloverOAuthClient(http).ExchangeAsync("APP_123", "secret", "code",
            DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("https://apisandbox.dev.clover.com/oauth/v2/token", requestedUrl?.ToString());
        Assert.Contains("\"client_secret\":\"secret\"", sentBody);
        Assert.Contains("\"code\":\"code\"", sentBody);
    }

    [Fact]
    public async Task DoesNotAcceptRedirectOrMalformedTokenResponse()
    {
        using var redirectHttp = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TemporaryRedirect))));
        var client = new CloverOAuthClient(redirectHttp);

        var result = await client.ExchangeAsync("APP_123", "secret", "code",
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshesOnlyAgainstOfficialSandboxEndpointWithoutSendingAppSecret()
    {
        Uri? requestedUrl = null;
        string? sentBody = null;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            requestedUrl = request.RequestUri;
            sentBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"access-2","refresh_token":"refresh-2","access_token_expiration":1800003600,"refresh_token_expiration":1800086400}"""),
            };
        }));

        var result = await new CloverOAuthClient(http).RefreshAsync("APP_123", "refresh-1",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("https://apisandbox.dev.clover.com/oauth/v2/refresh", requestedUrl?.ToString());
        Assert.Contains("\"client_id\":\"APP_123\"", sentBody);
        Assert.Contains("\"refresh_token\":\"refresh-1\"", sentBody);
        Assert.DoesNotContain("client_secret", sentBody);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
