using System.Security.Cryptography;
using System.Text;

namespace Carnicerias.Api.Payments;

/// <summary>Validates the Point webhook manifest; callers must still fetch the order from Mercado Pago.</summary>
public static class MercadoPagoWebhookSignature
{
    public static bool IsValid(string? signatureHeader, string? requestId,
        string? orderId, string? secret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || signatureHeader.Length > 512 ||
            string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128 ||
            string.IsNullOrWhiteSpace(orderId) || orderId.Length > 100 ||
            string.IsNullOrWhiteSpace(secret) || secret.Length > 4096 ||
            !orderId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
            requestId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            return false;

        string? timestamp = null;
        string? hash = null;
        foreach (var component in signatureHeader.Split(','))
        {
            var pair = component.Trim().Split('=', 2);
            if (pair.Length != 2)
                return false;
            if (pair[0] == "ts")
            {
                if (timestamp is not null) return false;
                timestamp = pair[1];
            }
            else if (pair[0] == "v1")
            {
                if (hash is not null) return false;
                hash = pair[1];
            }
        }

        if (timestamp is null || timestamp.Length is < 1 or > 20 ||
            !timestamp.All(char.IsAsciiDigit) || hash?.Length != 64)
            return false;

        if (!hash.All(Uri.IsHexDigit))
            return false;
        var supplied = Convert.FromHexString(hash);
        var manifest = $"id:{orderId};request-id:{requestId};ts:{timestamp};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(manifest));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
