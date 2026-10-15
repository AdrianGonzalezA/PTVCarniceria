using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Carnicerias.Api.Federation;

public sealed class BranchServiceSettings
{
    private readonly byte[]? credentialHash;
    private readonly byte[]? nextCredentialHash;

    private BranchServiceSettings(bool isRequested, bool isManaged, Guid installationId, Guid companyId,
        Guid branchId, byte[]? credentialHash, byte[]? nextCredentialHash)
    {
        IsRequested = isRequested;
        IsManaged = isManaged;
        InstallationId = installationId;
        CompanyId = companyId;
        BranchId = branchId;
        this.credentialHash = credentialHash;
        this.nextCredentialHash = nextCredentialHash;
    }

    public bool IsRequested { get; }
    public bool IsManaged { get; }
    public bool IsEnabled => IsManaged ? CompanyId != Guid.Empty && BranchId != Guid.Empty
        : credentialHash is not null;
    public Guid InstallationId { get; }
    public Guid CompanyId { get; }
    public Guid BranchId { get; }

    public static BranchServiceSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var isRequested = bool.TryParse(configuration["Federation:Enabled"], out var enabled) && enabled;
        if (!isRequested) return Disabled(false);

        var mode = configuration["Federation:Mode"];
        if (string.Equals(mode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(configuration["Federation:CompanyId"], out var managedCompanyId) ||
                !Guid.TryParse(configuration["Federation:BranchId"], out var managedBranchId) ||
                managedCompanyId == Guid.Empty || managedBranchId == Guid.Empty)
                return Disabled(true);
            return new BranchServiceSettings(true, true, Guid.Empty, managedCompanyId,
                managedBranchId, null, null);
        }
        if (!string.IsNullOrWhiteSpace(mode) &&
            !string.Equals(mode, "legacy", StringComparison.OrdinalIgnoreCase))
            return Disabled(true);

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

        return new BranchServiceSettings(true, false, installationId, companyId, branchId,
            credentialHash, nextCredentialHash);
    }

    public bool VerifyCredential(string? credential)
    {
        if (!IsEnabled || IsManaged || credential is null || credential.Length != 64 ||
            !TryReadHash(credential, out var tokenBytes)) return false;

        var hash = SHA256.HashData(tokenBytes);
        var valid = CryptographicOperations.FixedTimeEquals(hash, credentialHash!);
        if (nextCredentialHash is not null)
            valid |= CryptographicOperations.FixedTimeEquals(hash, nextCredentialHash);
        return valid;
    }

    private static BranchServiceSettings Disabled(bool isRequested) =>
        new(isRequested, false, Guid.Empty, Guid.Empty, Guid.Empty, null, null);

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
