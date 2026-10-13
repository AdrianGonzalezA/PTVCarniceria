using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Carnicerias.Api.Payments;

public sealed record CloverDeviceInfo(string Id, string Name, string Model);
public sealed record CloverDeviceInventoryResult(IReadOnlyList<CloverDeviceInfo> Devices, string? ErrorCode);

/// <summary>Read-only Platform API inventory. It does not indicate that Pay Display is installed or running.</summary>
public sealed class CloverDeviceInventoryClient(HttpClient httpClient)
{
    private const string SandboxBaseUrl = "https://apisandbox.dev.clover.com/v3/merchants/";

    public async Task<CloverDeviceInventoryResult> ListAsync(string merchantId, string accessToken,
        CancellationToken cancellationToken)
    {
        if (!CloverOAuthFlow.ValidMerchantId(merchantId))
            return Error("CLOVER_MERCHANT_MISMATCH");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            SandboxBaseUrl + merchantId + "/devices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("CarniceriasPOS/0.1 (sandbox-device-inventory)");
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Error("CLOVER_TOKEN_REJECTED");
        if (!response.IsSuccessStatusCode) return Error("CLOVER_UNAVAILABLE");
        await response.Content.LoadIntoBufferAsync(65_536, cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("elements", out var elements) ||
                elements.ValueKind != JsonValueKind.Array || elements.GetArrayLength() > 100)
                return Error("CLOVER_DEVICES_INVALID_RESPONSE");
            var devices = new List<CloverDeviceInfo>();
            foreach (var element in elements.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("id", out var idElement) ||
                    idElement.ValueKind != JsonValueKind.String ||
                    !CloverOAuthFlow.ValidMerchantId(idElement.GetString()))
                    return Error("CLOVER_DEVICES_INVALID_RESPONSE");
                devices.Add(new CloverDeviceInfo(idElement.GetString()!,
                    OptionalText(element, "name"), OptionalText(element, "model")));
            }
            return new CloverDeviceInventoryResult(devices, null);
        }
        catch (JsonException) { return Error("CLOVER_DEVICES_INVALID_RESPONSE"); }
        catch (InvalidOperationException) { return Error("CLOVER_DEVICES_INVALID_RESPONSE"); }
    }

    private static string OptionalText(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: <= 100 } text ? text : string.Empty;

    private static CloverDeviceInventoryResult Error(string code) => new([], code);
}
