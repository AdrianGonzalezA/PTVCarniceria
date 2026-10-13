using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Carnicerias.Api.Payments;

public enum CloverMerchantProbeOutcome { Verified, Unauthorized, MerchantMismatch, Unavailable }

/// <summary>Read-only sandbox check; never returns merchant data or credentials to the caller.</summary>
public sealed class CloverMerchantProbeClient(HttpClient httpClient)
{
    private const string SandboxBaseUrl = "https://apisandbox.dev.clover.com/v3/merchants/";

    public async Task<CloverMerchantProbeOutcome> ProbeAsync(string merchantId,
        string accessToken, CancellationToken cancellationToken)
    {
        if (!CloverOAuthFlow.ValidMerchantId(merchantId))
            return CloverMerchantProbeOutcome.MerchantMismatch;

        using var request = new HttpRequestMessage(HttpMethod.Get, SandboxBaseUrl + merchantId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("CarniceriasPOS/0.1 (sandbox-diagnostic)");
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return CloverMerchantProbeOutcome.Unauthorized;
        if (!response.IsSuccessStatusCode) return CloverMerchantProbeOutcome.Unavailable;

        await response.Content.LoadIntoBufferAsync(16_384, cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            return document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                string.Equals(id.GetString(), merchantId, StringComparison.Ordinal)
                ? CloverMerchantProbeOutcome.Verified
                : CloverMerchantProbeOutcome.MerchantMismatch;
        }
        catch (JsonException) { return CloverMerchantProbeOutcome.MerchantMismatch; }
        catch (InvalidOperationException) { return CloverMerchantProbeOutcome.MerchantMismatch; }
    }
}
