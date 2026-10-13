using System.Security.Cryptography;
using System.Text;

namespace Carnicerias.Api.Payments;

public static class CloverOAuthFlow
{
    public const string CallbackUrl = "https://clover-sandbox.nuestromundo.ar/clover/callback";
    private const string AuthorizeEndpoint = "https://sandbox.dev.clover.com/oauth/v2/authorize";

    public static string NewState() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashState(string state) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.ASCII.GetBytes(state)));

    public static string AuthorizeUrl(string clientId, string state) =>
        $"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(clientId)}" +
        $"&response_type=code&redirect_uri={Uri.EscapeDataString(CallbackUrl)}" +
        $"&state={Uri.EscapeDataString(state)}";

    public static bool ValidCallback(string? state, string? code, string? merchantId) =>
        state is { Length: 43 } && state.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_') &&
        code is { Length: > 0 and <= 2048 } && !code.Any(char.IsWhiteSpace) &&
        ValidMerchantId(merchantId);

    public static bool ValidMerchantId(string? merchantId) =>
        merchantId is { Length: > 0 and <= 100 } && merchantId.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
