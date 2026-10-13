using System.Net;
using Carnicerias.Api.Payments;

namespace Carnicerias.ArchitectureTests;

public sealed class CloverMerchantProbeClientTests
{
    [Fact]
    public async Task VerifiesOnlyTheConfiguredMerchantThroughTheOfficialSandboxApi()
    {
        Uri? requestedUrl = null;
        string? authorization = null;
        string? userAgent = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            requestedUrl = request.RequestUri;
            authorization = request.Headers.Authorization?.ToString();
            userAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"MERCHANT1234","name":"Comercio de prueba"}"""),
            };
        }));

        var outcome = await new CloverMerchantProbeClient(http).ProbeAsync(
            "MERCHANT1234", "secret-token", CancellationToken.None);

        Assert.Equal(CloverMerchantProbeOutcome.Verified, outcome);
        Assert.Equal("https://apisandbox.dev.clover.com/v3/merchants/MERCHANT1234", requestedUrl?.ToString());
        Assert.Equal("Bearer secret-token", authorization);
        Assert.Contains("CarniceriasPOS/0.1", userAgent);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CloverMerchantProbeOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, CloverMerchantProbeOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.TemporaryRedirect, CloverMerchantProbeOutcome.Unavailable)]
    public async Task DoesNotTreatRejectionOrRedirectAsVerified(HttpStatusCode status,
        CloverMerchantProbeOutcome expected)
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(status)));

        var outcome = await new CloverMerchantProbeClient(http).ProbeAsync(
            "MERCHANT1234", "secret-token", CancellationToken.None);

        Assert.Equal(expected, outcome);
    }

    [Theory]
    [InlineData("""{"id":"OTHER1234567"}""")]
    [InlineData("""{"id":42}""")]
    [InlineData("""{"name":"no identifier"}""")]
    public async Task RejectsUnexpectedMerchantIdentity(string body)
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        }));

        var outcome = await new CloverMerchantProbeClient(http).ProbeAsync(
            "MERCHANT1234", "secret-token", CancellationToken.None);

        Assert.Equal(CloverMerchantProbeOutcome.MerchantMismatch, outcome);
    }

    [Fact]
    public async Task InvalidMerchantIdNeverLeavesTheApi()
    {
        var called = false;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var outcome = await new CloverMerchantProbeClient(http).ProbeAsync(
            "../other", "secret-token", CancellationToken.None);

        Assert.Equal(CloverMerchantProbeOutcome.MerchantMismatch, outcome);
        Assert.False(called);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
