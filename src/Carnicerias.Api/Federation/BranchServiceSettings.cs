using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Carnicerias.Api.Federation;

public sealed class BranchServiceSettings
{
    private readonly byte[]? credentialHash;
    private readonly byte[]? nextCredentialHash;

    private BranchServiceSettings(bool isRequested, Guid installationId, Guid companyId,
        Guid branchId, byte[]? credentialHash, byte[]? nextCredentialHash)
    {
        IsRequested = isRequested;
        InstallationId = installationId;
        CompanyId = companyId;
        BranchId = branchId;
        this.credentialHash = credentialHash;
        this.nextCredentialHash = nextCredentialHash;
    }

    public bool IsRequested { get; }
    public bool IsEnabled => credentialHash is not null;
    public Guid InstallationId { get; }
    public Guid CompanyId { get; }
    public Guid BranchId { get; }

    public static BranchServiceSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var isRequested = bool.TryParse(configuration["Federation:Enabled"], out var enabled) && enabled;
        if (!isRequested) return Disabled(false);

        if (!Guid.TryParse(configuration["Federation:InstallationId"], out var installationId) ||
            !Guid.TryParse(configuration["Federation:CompanyId"], out var companyId) ||
            !Guid.TryParse(configuration["Federation:BranchId"], out var branchId) ||
            installationId == Guid.Empty || companyId == Guid.Empty || branchId == Guid.Empty ||
            !TryReadHash(configuration["Federation:CredentialSha256"], out var credentialHash))
            return Disabled(true);

        byte[]? nextCredentialHash = null;
        var next = configuration["Federation:NextCredentialSha256"];
        if (!string.IsNullOrWhiteSpace(next))
        {
            if (!TryReadHash(next, out var nextHash)) return Disabled(true);
            nextCredentialHash = nextHash;
        }

        return new BranchServiceSettings(true, installationId, companyId, branchId,
            credentialHash, nextCredentialHash);
    }

    public bool VerifyCredential(string? credential)
    {
        if (!IsEnabled || credential is null || credential.Length != 64 ||
            !TryReadHash(credential, out var tokenBytes)) return false;

        var hash = SHA256.HashData(tokenBytes);
        var valid = CryptographicOperations.FixedTimeEquals(hash, credentialHash!);
        if (nextCredentialHash is not null)
            valid |= CryptographicOperations.FixedTimeEquals(hash, nextCredentialHash);
        return valid;
    }

    private static BranchServiceSettings Disabled(bool isRequested) =>
        new(isRequested, Guid.Empty, Guid.Empty, Guid.Empty, null, null);

    private static bool TryReadHash(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is null || value.Length != 64) return false;
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
