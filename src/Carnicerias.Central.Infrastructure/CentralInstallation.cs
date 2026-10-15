namespace Carnicerias.Central.Infrastructure;

public sealed class CentralInstallation
{
    private CentralInstallation()
    {
        Label = string.Empty;
        DestinationUrl = string.Empty;
        SecretReference = string.Empty;
    }

    private CentralInstallation(Guid installationId, Guid centralCompanyId,
        Guid remoteCompanyId, Guid remoteBranchId, string label,
        string destinationUrl, string secretReference)
    {
        InstallationId = installationId;
        CentralCompanyId = centralCompanyId;
        RemoteCompanyId = remoteCompanyId;
        RemoteBranchId = remoteBranchId;
        Label = label;
        DestinationUrl = destinationUrl;
        SecretReference = secretReference;
        IsActive = true;
    }

    public Guid InstallationId { get; private set; }
    public Guid CentralCompanyId { get; private set; }
    public Guid RemoteCompanyId { get; private set; }
    public Guid RemoteBranchId { get; private set; }
    public string Label { get; private set; }
    public string DestinationUrl { get; private set; }
    public string SecretReference { get; private set; }
    public bool IsActive { get; private set; }

    public static CentralInstallation Create(Guid installationId, Guid centralCompanyId,
        Guid remoteCompanyId, Guid remoteBranchId, string? label,
        string? destinationUrl, string? secretReference)
    {
        if (installationId == Guid.Empty || centralCompanyId == Guid.Empty
            || remoteCompanyId == Guid.Empty || remoteBranchId == Guid.Empty)
        {
            throw new ArgumentException("Central and remote installation IDs are required.");
        }

        var normalizedLabel = (label ?? string.Empty).Trim();
        if (normalizedLabel.Length is < 1 or > 200 || normalizedLabel.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid installation label.", nameof(label));
        }

        if (destinationUrl is null || destinationUrl.Length > 2048
            || !Uri.TryCreate(destinationUrl, UriKind.Absolute, out var destination)
            || destination.Scheme is not ("https" or "http")
            || destination.UserInfo.Length > 0 || destination.Query.Length > 0
            || destination.Fragment.Length > 0 || destination.AbsolutePath != "/")
        {
            throw new ArgumentException("Invalid installation destination.", nameof(destinationUrl));
        }

        var normalizedReference = (secretReference ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedReference.Length is < 1 or > 200
            || normalizedReference.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('-' or '_')))
        {
            throw new ArgumentException("Invalid installation secret reference.", nameof(secretReference));
        }

        return new CentralInstallation(installationId, centralCompanyId,
            remoteCompanyId, remoteBranchId, normalizedLabel,
            destination.AbsoluteUri, normalizedReference);
    }

    public void Deactivate() => IsActive = false;
}
