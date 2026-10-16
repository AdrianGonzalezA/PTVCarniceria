using System.Net;
using System.Text;
using Carnicerias.Api.Payments;

namespace Carnicerias.IntegrationTests;

public sealed class MercadoPagoStaticQrClientTests
{
    [Fact]
    public async Task ReadsTheActiveIntegratedRegisterQrByExactExternalId()
    {
        var handler = new Handler("""
            {"data":[{"external_id":"SUC1CAJA1","status":"active",
              "config":{"qr":{"operating_mode":"pdv"}},
              "qr_response":{"qr_code":"000201STATIC"}}]}
            """);
        var qr = await new MercadoPagoStaticQrClient(new HttpClient(handler))
            .GetAsync("test-token", "SUC1CAJA1");
        Assert.Equal("000201STATIC", qr);
        Assert.Equal("https://api.mercadopago.com/v2/pos?external_id=SUC1CAJA1&limit=2", handler.Url);
    }

    [Theory]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[{\"external_id\":\"SUC1CAJA1\",\"status\":\"inactive\",\"config\":{\"qr\":{\"operating_mode\":\"pdv\"}},\"qr_response\":{\"qr_code\":\"000201STATIC\"}}]}")]
    [InlineData("{\"data\":[{\"external_id\":\"OTHER\",\"status\":\"active\",\"config\":{\"qr\":{\"operating_mode\":\"pdv\"}},\"qr_response\":{\"qr_code\":\"000201STATIC\"}}]}")]
    [InlineData("{\"data\":[{\"external_id\":123,\"status\":\"active\"}]}")]
    [InlineData("{\"data\":[{\"external_id\":\"SUC1CAJA1\",\"status\":\"active\",\"config\":null}]}")]
    [InlineData("{\"data\":[{\"external_id\":\"SUC1CAJA1\",\"status\":\"active\",\"config\":{\"qr\":null}}]}")]
    public async Task RefusesMissingInactiveOrDifferentRegister(string response)
    {
        var handler = new Handler(response);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MercadoPagoStaticQrClient(new HttpClient(handler))
                .GetAsync("test-token", "SUC1CAJA1"));
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
