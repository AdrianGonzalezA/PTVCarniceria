using System.Net;
using Carnicerias.Api.Payments;

namespace Carnicerias.ArchitectureTests;

public sealed class CloverDeviceInventoryClientTests
{
    [Fact]
    public async Task UsesOnlySandboxMerchantDevicesAndReturnsSanitizedFields()
    {
        Uri? requestedUrl = null;
        string? authorization = null;
        string? userAgent = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            requestedUrl = request.RequestUri;
            authorization = request.Headers.Authorization?.ToString();
            userAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"elements":[{"id":"DEV_1","name":"Mini pruebas","model":"Mini","serial":"PRIVATE"}]}""") };
        }));

        var result = await new CloverDeviceInventoryClient(http).ListAsync("MERCHANT_123", "access-token",
            CancellationToken.None);

        Assert.Equal("https://apisandbox.dev.clover.com/v3/merchants/MERCHANT_123/devices",
            requestedUrl?.ToString());
        Assert.Equal("Bearer access-token", authorization);
        Assert.Contains("CarniceriasPOS/0.1", userAgent);
        Assert.Null(result.ErrorCode);
        Assert.Single(result.Devices);
        Assert.Equal("DEV_1", result.Devices[0].Id);
        Assert.Equal("Mini pruebas", result.Devices[0].Name);
        Assert.DoesNotContain("PRIVATE", result.Devices[0].ToString());
    }

    [Fact]
    public async Task RejectsOtherMerchantMalformedPayloadAndUnauthorizedResult()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        var client = new CloverDeviceInventoryClient(http);
        Assert.Equal("CLOVER_MERCHANT_MISMATCH", (await client.ListAsync("../other", "token",
            CancellationToken.None)).ErrorCode);
        Assert.Equal("CLOVER_DEVICES_INVALID_RESPONSE", (await client.ListAsync("MERCHANT_123", "token",
            CancellationToken.None)).ErrorCode);

        using var unauthorizedHttp = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)));
        Assert.Equal("CLOVER_TOKEN_REJECTED", (await new CloverDeviceInventoryClient(unauthorizedHttp)
            .ListAsync("MERCHANT_123", "token", CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task EmptyInventoryIsValidAndRedirectIsNotFollowed()
    {
        using var emptyHttp = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"elements":[]}""") }));
        var empty = await new CloverDeviceInventoryClient(emptyHttp).ListAsync("MERCHANT_123", "token",
            CancellationToken.None);
        Assert.Null(empty.ErrorCode);
        Assert.Empty(empty.Devices);

        using var redirectHttp = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)));
        var redirect = await new CloverDeviceInventoryClient(redirectHttp).ListAsync("MERCHANT_123", "token",
            CancellationToken.None);
        Assert.Equal("CLOVER_UNAVAILABLE", redirect.ErrorCode);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
