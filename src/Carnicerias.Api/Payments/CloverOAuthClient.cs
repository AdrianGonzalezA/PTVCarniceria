using System.Net.Http.Json;
using System.Text.Json;

namespace Carnicerias.Api.Payments;

public sealed record CloverOAuthTokenResponse(string AccessToken, string RefreshToken,
    DateTimeOffset AccessExpiresAtUtc, DateTimeOffset RefreshExpiresAtUtc)
{
    public static CloverOAuthTokenResponse? Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("access_token", out var accessTokenElement) ||
                !root.TryGetProperty("refresh_token", out var refreshTokenElement) ||
                !root.TryGetProperty("access_token_expiration", out var accessExpiryElement) ||
                !root.TryGetProperty("refresh_token_expiration", out var refreshExpiryElement) ||
                accessTokenElement.ValueKind != JsonValueKind.String ||
                refreshTokenElement.ValueKind != JsonValueKind.String ||
                !accessExpiryElement.TryGetInt64(out var accessExpiry) ||
                !refreshExpiryElement.TryGetInt64(out var refreshExpiry)) return null;

            var accessToken = accessTokenElement.GetString();
            var refreshToken = refreshTokenElement.GetString();
            if (accessToken is not { Length: > 0 and <= 4096 } ||
                refreshToken is not { Length: > 0 and <= 4096 } ||
                accessToken.Any(char.IsWhiteSpace) || refreshToken.Any(char.IsWhiteSpace)) return null;

            var accessExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(accessExpiry);
            var refreshExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(refreshExpiry);
            return accessExpiresAtUtc > now && refreshExpiresAtUtc > accessExpiresAtUtc
                ? new CloverOAuthTokenResponse(accessToken, refreshToken,
                    accessExpiresAtUtc, refreshExpiresAtUtc) : null;
        }
        catch (JsonException) { return null; }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}

public sealed class CloverOAuthClient(HttpClient httpClient)
{
    private static readonly Uri TokenEndpoint = new("https://apisandbox.dev.clover.com/oauth/v2/token");
    private static readonly Uri RefreshEndpoint = new("https://apisandbox.dev.clover.com/oauth/v2/refresh");

    public async Task<CloverOAuthTokenResponse?> ExchangeAsync(string clientId,
        string clientSecret, string code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(TokenEndpoint,
            new { client_id = clientId, client_secret = clientSecret, code }, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await response.Content.LoadIntoBufferAsync(65_536, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return CloverOAuthTokenResponse.Parse(body, now);
    }

    public async Task<CloverOAuthTokenResponse?> RefreshAsync(string clientId,
        string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(RefreshEndpoint,
            new { client_id = clientId, refresh_token = refreshToken }, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await response.Content.LoadIntoBufferAsync(65_536, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return CloverOAuthTokenResponse.Parse(body, DateTimeOffset.UtcNow);
    }
}
