namespace Carnicerias.Central.Infrastructure;

public interface ICentralEncryptedBranchCredentialReader
{
    Task<CentralEncryptedBranchCredential?> FindAsync(Guid installationId,
        CancellationToken cancellationToken);
}

public interface ICentralBranchCredentialResolver
{
    Task<string> ResolveAsync(CentralInstallation installation,
        CancellationToken cancellationToken);
}

public static class CentralManagedSecretReference
{
    private const string Prefix = "DB_";

    public static string Create(Guid installationId)
    {
        if (installationId == Guid.Empty) throw Unavailable();
        return Prefix + installationId.ToString("N").ToUpperInvariant();
    }

    public static bool IsManaged(CentralInstallation installation)
    {
        if (!installation.SecretReference.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        if (installation.SecretReference != Create(installation.InstallationId))
            throw Unavailable();
        return true;
    }

    private static InvalidOperationException Unavailable() =>
        new("Central branch credential is unavailable.");
}

public sealed class CentralBranchCredentialResolver(
    ICentralEncryptedBranchCredentialReader encryptedReader,
    ICentralBranchCredentialSource legacySource,
    Func<CentralBranchCredentialCrypto> cryptoFactory) : ICentralBranchCredentialResolver
{
    public async Task<string> ResolveAsync(CentralInstallation installation,
        CancellationToken cancellationToken)
    {
        string? token;
        if (CentralManagedSecretReference.IsManaged(installation))
        {
            var envelope = await encryptedReader.FindAsync(installation.InstallationId,
                cancellationToken) ?? throw Unavailable();
            token = cryptoFactory().Decrypt(installation.CentralCompanyId,
                installation.InstallationId, envelope);
        }
        else
        {
            token = legacySource.Read(installation.SecretReference);
        }

        return token is { Length: 64 } && token.All(Uri.IsHexDigit)
            ? token : throw Unavailable();
    }

    private static InvalidOperationException Unavailable() =>
        new("Central branch credential is unavailable.");
}
