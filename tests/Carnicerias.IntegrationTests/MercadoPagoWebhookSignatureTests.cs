using System.Security.Cryptography;
using System.Text;
using Carnicerias.Api.Payments;

namespace Carnicerias.IntegrationTests;

public sealed class MercadoPagoWebhookSignatureTests
{
    [Fact]
    public void AcceptsOnlyTheSignedOrderIdAndRequest()
    {
        const string secret = "local-test-webhook-secret";
        const string id = "ORD01JQ4S4KY8HWQ6NA5PXB65B3D3";
        const string requestId = "request-123";
        const string timestamp = "1742505638683";
        var payload = $"id:{id};request-id:{requestId};ts:{timestamp};";
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

        Assert.True(MercadoPagoWebhookSignature.IsValid(
            $"ts={timestamp},v1={signature}", requestId, id, secret));
        Assert.False(MercadoPagoWebhookSignature.IsValid(
            $"ts={timestamp},v1={signature}", requestId, id + "X", secret));
        Assert.False(MercadoPagoWebhookSignature.IsValid(
            $"ts={timestamp},v1={signature}", "other-request", id, secret));
        Assert.False(MercadoPagoWebhookSignature.IsValid(
            $"ts={timestamp},v1={new string('0', 64)}", requestId, id, secret));
    }

    [Theory]
    [InlineData("", "request", "ORD123", "secret")]
    [InlineData("ts=1,v1=bad", "request", "ORD123", "secret")]
    [InlineData("ts=1,v1=0000000000000000000000000000000000000000000000000000000000000000", "", "ORD123", "secret")]
    [InlineData("ts=1,v1=0000000000000000000000000000000000000000000000000000000000000000", "request", "", "secret")]
    [InlineData("ts=1,v1=0000000000000000000000000000000000000000000000000000000000000000", "request", "ORD123", "")]
    public void RejectsMalformedOrMissingInputs(string signature, string requestId,
        string orderId, string secret)
    {
        Assert.False(MercadoPagoWebhookSignature.IsValid(signature, requestId, orderId, secret));
    }
}
