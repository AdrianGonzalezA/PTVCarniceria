using System.Globalization;
using System.Security.Cryptography;

namespace Carnicerias.Central.Infrastructure;

public sealed record CentralEncryptedBranchCredential(int KeyVersion, byte[] Nonce,
    byte[] Ciphertext, byte[] Tag);

public sealed class CentralBranchCredentialCrypto(int activeKeyVersion,
    Func<int, byte[]?> readKey)
{
    private const int KeyBytes = 32;
    private const int TokenBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    public static CentralBranchCredentialCrypto FromEnvironment()
    {
        var configuredVersion = Environment.GetEnvironmentVariable(
            "CARNICERIAS_CENTRAL_CREDENTIAL_ACTIVE_KEY_VERSION");
        var activeVersion = 1;
        if (configuredVersion is not null
            && (!int.TryParse(configuredVersion, NumberStyles.None,
                CultureInfo.InvariantCulture, out activeVersion) || activeVersion < 1))
            throw Unavailable();
        return new CentralBranchCredentialCrypto(activeVersion, version =>
        {
            var hex = Environment.GetEnvironmentVariable(
                $"CARNICERIAS_CENTRAL_CREDENTIAL_KEY_V{version}");
            try { return hex is null ? null : Convert.FromHexString(hex); }
            catch (FormatException) { throw Unavailable(); }
        });
    }

    public CentralEncryptedBranchCredential Encrypt(Guid centralCompanyId,
        Guid installationId, string? token)
    {
        if (centralCompanyId == Guid.Empty || installationId == Guid.Empty
            || token is null || token.Length != TokenBytes * 2
            || !token.All(Uri.IsHexDigit)) throw Unavailable();
        var key = ReadValidKey(activeKeyVersion);
        var plaintext = Convert.FromHexString(token);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[TokenBytes];
        var tag = new byte[TagBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag,
                AssociatedData(centralCompanyId, installationId));
            return new CentralEncryptedBranchCredential(activeKeyVersion,
                nonce, ciphertext, tag);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public string Decrypt(Guid centralCompanyId, Guid installationId,
        CentralEncryptedBranchCredential encrypted)
    {
        if (centralCompanyId == Guid.Empty || installationId == Guid.Empty
            || encrypted.KeyVersion < 1 || encrypted.Nonce.Length != NonceBytes
            || encrypted.Ciphertext.Length != TokenBytes || encrypted.Tag.Length != TagBytes)
            throw Unavailable();
        var key = ReadValidKey(encrypted.KeyVersion);
        var plaintext = new byte[TokenBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(encrypted.Nonce, encrypted.Ciphertext, encrypted.Tag,
                plaintext, AssociatedData(centralCompanyId, installationId));
            return Convert.ToHexString(plaintext);
        }
        catch (CryptographicException) { throw Unavailable(); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private byte[] ReadValidKey(int version)
    {
        var key = readKey(version);
        return activeKeyVersion < 1 || key is not { Length: KeyBytes }
            ? throw Unavailable() : key;
    }

    private static byte[] AssociatedData(Guid centralCompanyId, Guid installationId)
    {
        var bytes = new byte[32];
        _ = centralCompanyId.TryWriteBytes(bytes.AsSpan(0, 16));
        _ = installationId.TryWriteBytes(bytes.AsSpan(16, 16));
        return bytes;
    }

    private static InvalidOperationException Unavailable() =>
        new("Central branch credential is unavailable.");
}
