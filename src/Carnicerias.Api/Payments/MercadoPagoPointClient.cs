using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Carnicerias.Api.Payments;

public sealed class PointOrderRequest
{
    public PointOrderRequest(string terminalId, decimal amount, string externalReference,
        Guid idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(terminalId) || terminalId.Length > 100 ||
            !terminalId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
            !Guid.TryParseExact(externalReference, "N", out _) ||
            amount < 15m || amount > 999_999_999.99m || decimal.Round(amount, 2) != amount ||
            idempotencyKey == Guid.Empty)
            throw new ArgumentException("An explicit terminal, sale reference, amount and idempotency key are required.");
        TerminalId = terminalId;
        Amount = amount;
        ExternalReference = externalReference;
        IdempotencyKey = idempotencyKey;
    }

    public string TerminalId { get; }
    public decimal Amount { get; }
    public string ExternalReference { get; }
    public Guid IdempotencyKey { get; }
}

public sealed record PointOrderState(string Id, string Status, string PaymentId,
    string PaymentStatus, string? PaymentStatusDetail, decimal Amount,
    string ExternalReference)
{
    public bool IsApproved => Status == "processed" && PaymentStatus == "processed" &&
        PaymentStatusDetail == "accredited";
}

/// <summary>Point Orders transport. It does not mark a POS sale as paid.</summary>
public sealed class MercadoPagoPointClient(HttpClient httpClient)
{
    private const string Origin = "https://api.mercadopago.com";

    public async Task<PointOrderState> CreateAsync(string accessToken,
        PointOrderRequest order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        using var request = NewRequest(HttpMethod.Post, "/v1/orders", accessToken);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key",
            order.IdempotencyKey.ToString("D"));
        request.Content = JsonContent.Create(new
        {
            type = "point",
            external_reference = order.ExternalReference,
            transactions = new { payments = new[] { new
            {
                amount = order.Amount.ToString("0.00", CultureInfo.InvariantCulture)
            } } },
            config = new { point = new { terminal_id = order.TerminalId,
                print_on_terminal = "no_ticket" } }
        });
        return await SendAsync(request, null, order.ExternalReference,
            order.Amount, cancellationToken);
    }

    public async Task<PointOrderState> GetAsync(string accessToken, string orderId,
        string expectedExternalReference, decimal expectedAmount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 100 ||
            !orderId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
            !Guid.TryParseExact(expectedExternalReference, "N", out _) ||
            expectedAmount <= 0 || expectedAmount > 999_999_999.99m ||
            decimal.Round(expectedAmount, 2) != expectedAmount)
            throw new ArgumentException("A valid order id, sale reference and expected amount are required.");
        using var request = NewRequest(HttpMethod.Get, $"/v1/orders/{orderId}", accessToken);
        return await SendAsync(request, orderId, expectedExternalReference,
            expectedAmount, cancellationToken);
    }

    public async Task<PointOrderState> CancelAsync(string accessToken, string orderId,
        string expectedExternalReference, decimal expectedAmount, Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(orderId) || orderId.Length > 100 ||
            !orderId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            throw new ArgumentException("A Point order and cancellation idempotency key are required.");
        using var request = NewRequest(HttpMethod.Post, $"/v1/orders/{orderId}/cancel", accessToken);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey.ToString("D"));
        request.Headers.TryAddWithoutValidation("x-allow-cancelable-status", "at_terminal");
        return await SendAsync(request, orderId, expectedExternalReference, expectedAmount,
            cancellationToken);
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string path, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 4096 ||
            accessToken.Any(char.IsWhiteSpace))
            throw new ArgumentException("A valid provider access token is required.", nameof(accessToken));
        var request = new HttpRequestMessage(method, Origin + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<PointOrderState> SendAsync(HttpRequestMessage request, string? expectedOrderId,
        string expectedExternalReference, decimal expectedAmount,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_000_000, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
        var root = document.RootElement;
        var id = Text(root, "id");
        var type = Text(root, "type");
        var reference = Text(root, "external_reference");
        var status = Text(root, "status");
        var payments = root.GetProperty("transactions").GetProperty("payments");
        if (payments.ValueKind != JsonValueKind.Array || payments.GetArrayLength() != 1)
            throw new InvalidDataException("Point returned an unexpected payment count.");
        var payment = payments[0];
        var paymentId = Text(payment, "id");
        var paymentStatus = Text(payment, "status");
        var detail = payment.TryGetProperty("status_detail", out var statusDetail)
            ? statusDetail.GetString() : null;
        if (!decimal.TryParse(Text(payment, "amount"), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) ||
            type != "point" || reference != expectedExternalReference || amount != expectedAmount ||
            (expectedOrderId is not null && id != expectedOrderId))
            throw new InvalidDataException("Point returned an order for another sale or amount.");
        return new PointOrderState(id, status, paymentId, paymentStatus, detail,
            amount, reference);
    }

    private static string Text(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String &&
        item.GetString() is { Length: > 0 } text
            ? text : throw new InvalidDataException($"Point omitted {property}.");
}
