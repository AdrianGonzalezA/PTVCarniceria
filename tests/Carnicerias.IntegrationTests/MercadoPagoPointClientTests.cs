using System.Net;
using System.Text;
using Carnicerias.Api.Payments;

namespace Carnicerias.IntegrationTests;

public sealed class MercadoPagoPointClientTests
{
    [Fact]
    public async Task CreatesAnIdempotentOrderWithoutTreatingCreatedAsPaid()
    {
        var handler = new FakeHandler("""
            {"id":"ORD123","type":"point","external_reference":"0123456789abcdef0123456789abcdef",
             "status":"created","transactions":{"payments":[{"id":"PAY123","amount":"2375.00",
             "status":"created","status_detail":"created"}]}}
            """);
        var client = new MercadoPagoPointClient(new HttpClient(handler));
        var key = Guid.NewGuid();
        var request = new PointOrderRequest("TERMINAL_1", 2375m,
            "0123456789abcdef0123456789abcdef", key);

        var order = await client.CreateAsync("test-access-token", request);

        Assert.False(order.IsApproved);
        Assert.Equal("created", order.Status);
        Assert.Equal("https://api.mercadopago.com/v1/orders", handler.Url);
        Assert.Equal(key.ToString("D"), handler.IdempotencyKey);
        Assert.Contains("TERMINAL_1", handler.Body);
        Assert.Contains("\"amount\":\"2375.00\"", handler.Body);
        Assert.DoesNotContain("test-access-token", handler.Url);
    }

    [Fact]
    public async Task RequiresBothProcessedOrderAndAccreditedPayment()
    {
        var handler = new FakeHandler("""
            {"id":"ORD123","type":"point","external_reference":"0123456789abcdef0123456789abcdef",
             "status":"processed","transactions":{"payments":[{"id":"PAY123","amount":"2375.00",
             "status":"processed","status_detail":"accredited"}]}}
            """);
        var client = new MercadoPagoPointClient(new HttpClient(handler));

        var order = await client.GetAsync("test-access-token", "ORD123",
            "0123456789abcdef0123456789abcdef", 2375m);

        Assert.True(order.IsApproved);
        Assert.Equal("PAY123", order.PaymentId);
        Assert.Null(handler.IdempotencyKey);
        Assert.Equal("https://api.mercadopago.com/v1/orders/ORD123", handler.Url);
    }

    [Fact]
    public async Task RejectsAnOrderForAnotherAmountOrSale()
    {
        var handler = new FakeHandler("""
            {"id":"ORD123","type":"point","external_reference":"another-sale",
             "status":"processed","transactions":{"payments":[{"id":"PAY123","amount":"2375.00",
             "status":"processed","status_detail":"accredited"}]}}
            """);
        var client = new MercadoPagoPointClient(new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync("token", "ORD123",
            "0123456789abcdef0123456789abcdef", 2375m));
    }

    private sealed class FakeHandler(string response) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";
        public string Body { get; private set; } = "";
        public string? IdempotencyKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            IdempotencyKey = request.Headers.TryGetValues("X-Idempotency-Key", out var values)
                ? values.Single() : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
