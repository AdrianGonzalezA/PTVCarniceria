using System.Net;
using System.Text;
using Carnicerias.Api.Payments;

namespace Carnicerias.IntegrationTests;

public sealed class MercadoPagoQrClientTests
{
    [Fact]
    public async Task CreatesDynamicQrOrderAndDoesNotTreatCreatedAsPaid()
    {
        var handler = new Handler("""
            {"id":"ORD123","type":"qr","external_reference":"0123456789abcdef0123456789abcdef",
             "status":"created","type_response":{"qr_data":"000201TESTQR"},
             "transactions":{"payments":[{"id":"PAY123","amount":"2500.00",
             "status":"created","status_detail":"created"}]}}
            """);
        var client = new MercadoPagoQrClient(new HttpClient(handler));
        var order = await client.CreateAsync("test-token", new QrOrderRequest(
            "SUC1CAJA1", 2500m, "0123456789abcdef0123456789abcdef", Guid.NewGuid()));
        Assert.False(order.IsApproved);
        Assert.Equal("000201TESTQR", order.QrData);
        Assert.Contains("\"mode\":\"dynamic\"", handler.Body);
        Assert.Contains("\"external_pos_id\":\"SUC1CAJA1\"", handler.Body);
        Assert.Equal("https://api.mercadopago.com/v1/orders", handler.Url);
    }

    [Fact]
    public async Task RequiresMatchingAmountAndReferenceWhenCheckingApproval()
    {
        var handler = new Handler("""
            {"id":"ORD123","type":"qr","external_reference":"0123456789abcdef0123456789abcdef",
             "status":"processed","status_detail":"processed",
             "transactions":{"payments":[{"id":"PAY123","amount":"2500.00",
             "status":"processed","status_detail":"accredited"}]}}
            """);
        var client = new MercadoPagoQrClient(new HttpClient(handler));
        var order = await client.GetAsync("test-token", "ORD123",
            "0123456789abcdef0123456789abcdef", 2500m);
        Assert.True(order.IsApproved);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync("test-token", "ORD123",
            "0123456789abcdef0123456789abcdef", 2501m));
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
