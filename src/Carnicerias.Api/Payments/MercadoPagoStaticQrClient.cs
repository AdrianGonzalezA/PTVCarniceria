using System.Net.Http.Headers;
using System.Text.Json;

namespace Carnicerias.Api.Payments;

/// <summary>Reads the permanent QR of an active, integrated Mercado Pago register.</summary>
public sealed class MercadoPagoStaticQrClient(HttpClient httpClient)
{
    public async Task<string> GetAsync(string token, string externalPosId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsWhiteSpace) ||
            string.IsNullOrWhiteSpace(externalPosId) || externalPosId.Length > 40 ||
            !externalPosId.All(character => char.IsAsciiLetterOrDigit(character) ||
                character is '_' or '-'))
            throw new ArgumentException("A seller token and external register ID are required.");

        var url = $"https://api.mercadopago.com/v2/pos?external_id={externalPosId}&limit=2";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_000_000, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var registers) ||
            registers.ValueKind != JsonValueKind.Array || registers.GetArrayLength() != 1)
            throw new InvalidDataException("QR register search returned no unique register.");
        var register = registers[0];
        if (register.ValueKind != JsonValueKind.Object ||
            !register.TryGetProperty("external_id", out var id) ||
            id.ValueKind != JsonValueKind.String || id.GetString() != externalPosId ||
            !register.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String || status.GetString() != "active" ||
            !register.TryGetProperty("config", out var config) ||
            config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("qr", out var qrConfig) ||
            qrConfig.ValueKind != JsonValueKind.Object ||
            !qrConfig.TryGetProperty("operating_mode", out var operatingMode) ||
            operatingMode.ValueKind != JsonValueKind.String ||
            operatingMode.GetString() != "pdv" ||
            !register.TryGetProperty("qr_response", out var qrResponse) ||
            qrResponse.ValueKind != JsonValueKind.Object ||
            !qrResponse.TryGetProperty("qr_code", out var qrCode) ||
            qrCode.ValueKind != JsonValueKind.String ||
            qrCode.GetString() is not { Length: > 0 and <= 4096 } code ||
            code.Any(char.IsControl))
            throw new InvalidDataException("The register has no active integrated static QR.");
        return code;
    }
}
