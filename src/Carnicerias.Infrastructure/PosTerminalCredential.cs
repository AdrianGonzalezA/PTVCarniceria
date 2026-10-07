using System.Security.Cryptography;
using System.Text;

namespace Carnicerias.Infrastructure;

public sealed record PosTerminalCredential(string Token, string Hash)
{
    public override string ToString() => nameof(PosTerminalCredential);

    public static PosTerminalCredential Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            var token = Convert.ToBase64String(bytes).TrimEnd('=')
                .Replace('+', '-').Replace('/', '_');
            return new PosTerminalCredential(token, ComputeHash(token));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static string ComputeHash(string token)
    {
        if (token.Length != 43 || !token.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new ArgumentException("Invalid terminal credential.", nameof(token));

        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)))
            .ToLowerInvariant();
    }
}
