using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Carnicerias.Central.Infrastructure;

public interface ICentralPasswordHasher
{
    string Hash(string password);
    bool Verify(string? password, string? encodedHash);
}

public sealed class CentralPasswordHasher : ICentralPasswordHasher
{
    private readonly int minimumPasswordCharacters;
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

    public CentralPasswordHasher() : this(12) { }

    public CentralPasswordHasher(int minimumPasswordCharacters)
    {
        if (minimumPasswordCharacters is not (6 or 12))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPasswordCharacters));
        }

        this.minimumPasswordCharacters = minimumPasswordCharacters;
    }

    public string Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < minimumPasswordCharacters
            || Encoding.UTF8.GetByteCount(password) > MaximumPasswordBytes)
        {
            throw new ArgumentException("Central password does not meet the input policy.", nameof(password));
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        try
        {
            // NSec Argon2id API: https://nsec.rocks/docs/api/nsec.cryptography.passwordbasedkeyderivationalgorithm
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
        if (string.IsNullOrEmpty(password)
            || Encoding.UTF8.GetByteCount(password) > MaximumPasswordBytes
            || !TryParse(encodedHash, out var salt, out var expectedHash))
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
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
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
        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id"
            || parts[2] != "v=19" || parts[3] != ParametersText)
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
            // Invalid hash format is an authentication failure.
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
