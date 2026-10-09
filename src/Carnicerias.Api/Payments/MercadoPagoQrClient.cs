using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Carnicerias.Api.Payments;

public sealed class QrOrderRequest
{
    public QrOrderRequest(string externalPosId, decimal amount, string externalReference,
        Guid idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(externalPosId) || externalPosId.Length > 40 ||
            !externalPosId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
            !Guid.TryParseExact(externalReference, "N", out _) ||
            amount <= 0 || amount > 999_999_999.99m || decimal.Round(amount, 2) != amount ||
            idempotencyKey == Guid.Empty)
            throw new ArgumentException("QR needs a register, sale reference, exact amount and idempotency key.");
        ExternalPosId = externalPosId;
        Amount = amount;
        ExternalReference = externalReference;
        IdempotencyKey = idempotencyKey;
    }

    public string ExternalPosId { get; }
    public decimal Amount { get; }
    public string ExternalReference { get; }
    public Guid IdempotencyKey { get; }
}

public sealed record QrOrderState(string Id, string Status, string PaymentId,
    string PaymentStatus, string? PaymentStatusDetail, decimal Amount,
    string ExternalReference, string? QrData)
{
    public bool IsApproved => Status == "processed" && PaymentStatus == "processed" &&
        PaymentStatusDetail == "accredited";
}

/// <summary>Transport for Mercado Pago Orders QR; approval must be verified server-side.</summary>
public sealed class MercadoPagoQrClient(HttpClient httpClient)
{
    private const string Origin = "https://api.mercadopago.com";

    public async Task<QrOrderState> CreateAsync(string token, QrOrderRequest order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        using var request = NewRequest(HttpMethod.Post, "/v1/orders", token);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", order.IdempotencyKey.ToString("D"));
        request.Content = JsonContent.Create(new
        {
            type = "qr",
            total_amount = order.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            external_reference = order.ExternalReference,
            config = new { qr = new { external_pos_id = order.ExternalPosId, mode = "dynamic" } },
            transactions = new { payments = new[] { new
            {
                amount = order.Amount.ToString("0.00", CultureInfo.InvariantCulture)
            } } }
        });
        return await SendAsync(request, null, order.ExternalReference, order.Amount, cancellationToken);
    }

    public async Task<QrOrderState> GetAsync(string token, string orderId,
        string expectedReference, decimal expectedAmount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 100 ||
            !orderId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
            !Guid.TryParseExact(expectedReference, "N", out _) || expectedAmount <= 0)
            throw new ArgumentException("Invalid QR order lookup.");
        using var request = NewRequest(HttpMethod.Get, $"/v1/orders/{orderId}", token);
        return await SendAsync(request, orderId, expectedReference, expectedAmount, cancellationToken);
    }

    public async Task<QrOrderState> CancelAsync(string token, string orderId,
        string expectedReference, decimal expectedAmount, Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(orderId) || orderId.Length > 100 ||
            !orderId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            throw new ArgumentException("A QR order and cancellation idempotency key are required.");
        using var request = NewRequest(HttpMethod.Post, $"/v1/orders/{orderId}/cancel", token);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey.ToString("D"));
        return await SendAsync(request, orderId, expectedReference, expectedAmount, cancellationToken);
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string path, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("A valid provider access token is required.", nameof(token));
        var request = new HttpRequestMessage(method, Origin + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<QrOrderState> SendAsync(HttpRequestMessage request, string? expectedId,
        string expectedReference, decimal expectedAmount, CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_000_000, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
        var root = document.RootElement;
        var id = Text(root, "id");
        var reference = Text(root, "external_reference");
        var payments = root.GetProperty("transactions").GetProperty("payments");
        if (payments.ValueKind != JsonValueKind.Array || payments.GetArrayLength() != 1)
            throw new InvalidDataException("QR returned an unexpected payment count.");
        var payment = payments[0];
        if (!decimal.TryParse(Text(payment, "amount"), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) ||
            Text(root, "type") != "qr" || reference != expectedReference ||
            amount != expectedAmount || expectedId is not null && id != expectedId)
            throw new InvalidDataException("QR returned an order for another sale or amount.");
        string? qrData = null;
        if (root.TryGetProperty("type_response", out var typeResponse) &&
            typeResponse.TryGetProperty("qr_data", out var qrValue) &&
            qrValue.ValueKind == JsonValueKind.String)
            qrData = qrValue.GetString();
        if (qrData?.Length > 4096) throw new InvalidDataException("QR data is too large.");
        return new QrOrderState(id, Text(root, "status"), Text(payment, "id"),
            Text(payment, "status"), payment.TryGetProperty("status_detail", out var detail)
                ? detail.GetString() : null, amount, reference, qrData);
    }

    private static string Text(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String &&
        item.GetString() is { Length: > 0 } text
            ? text : throw new InvalidDataException($"QR omitted {property}.");
}
