using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Carnicerias.Api.Payments;

/// <summary>Finds an order whose create response was lost, using its persisted external reference.</summary>
public sealed class MercadoPagoOrderLookupClient(HttpClient httpClient)
{
    public async Task<string?> FindIdAsync(string token, string externalReference, string expectedType,
        DateTimeOffset intentCreatedAtUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsWhiteSpace) ||
            !Guid.TryParseExact(externalReference, "N", out _) ||
            expectedType is not "point" and not "qr" || intentCreatedAtUtc == default)
            throw new ArgumentException("A valid seller, reference and order type are required.");
        var begin = Uri.EscapeDataString(intentCreatedAtUtc.AddMinutes(-5).ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var end = Uri.EscapeDataString(intentCreatedAtUtc.AddHours(1).ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var url = $"https://api.mercadopago.com/v1/orders?begin_date={begin}&end_date={end}" +
            $"&external_reference={externalReference}&limit=10";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_000_000, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var orders) ||
            orders.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Order search did not return an array.");
        string? orderId = null;
        foreach (var order in orders.EnumerateArray())
        {
            if (order.ValueKind != JsonValueKind.Object ||
                !order.TryGetProperty("external_reference", out var reference) ||
                reference.GetString() != externalReference ||
                !order.TryGetProperty("type", out var type) || type.GetString() != expectedType)
                continue;
            if (!order.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } candidate ||
                candidate.Length > 100 || !candidate.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '_' or '-') || orderId is not null)
                throw new InvalidDataException("Order search returned ambiguous or invalid matches.");
            orderId = candidate;
        }
        return orderId;
    }
}
