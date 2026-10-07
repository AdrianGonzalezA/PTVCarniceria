using System.Security.Cryptography;
using System.Text;
using Carnicerias.PlatformAccess;
using NSec.Cryptography;

namespace Carnicerias.Infrastructure;

public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const int MaximumPasswordBytes = 1024;
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const string ParametersText = "m=19456,t=2,p=1";
    private static readonly Argon2Parameters Parameters = new()
    {
        MemorySize = 19 * 1024,
        NumberOfPasses = 2,
        DegreeOfParallelism = 1
    };

    public string Hash(string password)
    {
        ValidatePassword(password);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        try
        {
            var hash = PasswordBasedKeyDerivationAlgorithm.Argon2id(Parameters)
                .DeriveBytes(passwordBytes, salt, HashLength);
            try
            {
                return $"$argon2id$v=19${ParametersText}${Encode(salt)}${Encode(hash)}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public bool Verify(string? password, string? encodedHash)
    {
        if (string.IsNullOrEmpty(password) ||
            Encoding.UTF8.GetByteCount(password) > MaximumPasswordBytes ||
            !TryParse(encodedHash, out var salt, out var expectedHash))
        {
            return false;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var actualHash = PasswordBasedKeyDerivationAlgorithm.Argon2id(Parameters)
                .DeriveBytes(passwordBytes, salt, HashLength);
            try
            {
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actualHash);
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedHash);
        }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        if (Encoding.UTF8.GetByteCount(password) > MaximumPasswordBytes)
        {
            throw new ArgumentException("Password exceeds the supported input limit.", nameof(password));
        }
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static bool TryParse(string? encodedHash, out byte[] salt, out byte[] hash)
    {
        salt = [];
        hash = [];
        if (encodedHash is null || encodedHash.Length > 256)
        {
            return false;
        }

        var parts = encodedHash.Split('$');
        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id" ||
            parts[2] != "v=19" || parts[3] != ParametersText)
        {
            return false;
        }

        try
        {
            salt = Decode(parts[4]);
            hash = Decode(parts[5]);
            if (salt.Length == SaltLength && hash.Length == HashLength)
            {
                return true;
            }
        }
        catch (FormatException)
        {
            // Malformed hashes are treated as invalid credentials.
        }

        CryptographicOperations.ZeroMemory(salt);
        CryptographicOperations.ZeroMemory(hash);
        salt = [];
        hash = [];
        return false;
    }

    private static byte[] Decode(string value)
    {
        var paddingLength = (4 - value.Length % 4) % 4;
        return Convert.FromBase64String(value + new string('=', paddingLength));
    }
}
