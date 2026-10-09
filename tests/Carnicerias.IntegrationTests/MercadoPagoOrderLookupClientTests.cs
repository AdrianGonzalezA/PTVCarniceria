using System.Net;
using System.Text;
using Carnicerias.Api.Payments;

namespace Carnicerias.IntegrationTests;

public sealed class MercadoPagoOrderLookupClientTests
{
    private const string Reference = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task FindsOnlyTheSameReferenceAndPaymentType()
    {
        var handler = new Handler("""
            {"data":[{"id":"ORD_WRONG","type":"qr","external_reference":"0123456789abcdef0123456789abcdef"},
             {"id":"ORD_MATCH","type":"point","external_reference":"0123456789abcdef0123456789abcdef"}]}
            """);
        var client = new MercadoPagoOrderLookupClient(new HttpClient(handler));
        var id = await client.FindIdAsync("test-token", Reference, "point", DateTimeOffset.UtcNow);
        Assert.Equal("ORD_MATCH", id);
        Assert.Contains($"external_reference={Reference}", handler.Url);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task RefusesAmbiguousMatchesInsteadOfGuessingWhichPaymentWasCreated()
    {
        var handler = new Handler("""
            {"data":[{"id":"ORD_1","type":"point","external_reference":"0123456789abcdef0123456789abcdef"},
             {"id":"ORD_2","type":"point","external_reference":"0123456789abcdef0123456789abcdef"}]}
            """);
        var client = new MercadoPagoOrderLookupClient(new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.FindIdAsync(
            "test-token", Reference, "point", DateTimeOffset.UtcNow));
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";
        public string? AuthorizationScheme { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
