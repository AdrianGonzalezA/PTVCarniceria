namespace Carnicerias.Central.Infrastructure;

public sealed record CentralBranchConnectionCreate(Guid InstallationId, Guid RemoteCompanyId,
    Guid RemoteBranchId, string Label, string DestinationUrl, string Token);

public sealed record CentralBranchConnectionView(Guid InstallationId, Guid RemoteCompanyId,
    Guid RemoteBranchId, string Label, string DestinationUrl, bool IsActive,
    bool HasCredential);

public sealed class CentralBranchConnectionVerificationException : Exception;
public sealed class CentralBranchConnectionConflictException : Exception;

public interface ICentralInstallationVerifier
{
    Task VerifyAsync(CentralApprovedDestination destination, string token,
        Guid installationId, Guid companyId, Guid branchId,
        CancellationToken cancellationToken);
}

public sealed class PinnedCentralInstallationVerifier : ICentralInstallationVerifier
{
    public async Task VerifyAsync(CentralApprovedDestination destination, string token,
        Guid installationId, Guid companyId, Guid branchId,
        CancellationToken cancellationToken) =>
        _ = await CentralInstallationProbe.VerifyAsync(destination, token,
            installationId, companyId, branchId, cancellationToken);
}

public interface ICentralBranchConnectionStore
{
    Task<CentralManagementPage<CentralBranchConnectionView>> ListAsync(
        Guid centralCompanyId, int page, int pageSize, CancellationToken cancellationToken);
    Task SaveRegisteredAsync(CentralInstallation installation,
        CentralBranchCredential credential, CentralAuditEvent audit,
        CancellationToken cancellationToken);
    Task RecordRejectedAsync(CentralAuditEvent audit, CancellationToken cancellationToken);
}

public sealed class CentralBranchConnectionService(
    ICentralBranchConnectionStore store,
    ICentralInstallationVerifier verifier,
    CentralDestinationPolicy destinationPolicy,
    Func<CentralBranchCredentialCrypto> cryptoFactory,
    TimeProvider clock)
{
    public Task<CentralManagementPage<CentralBranchConnectionView>> ListAsync(
        Guid centralCompanyId, int page, int pageSize, CancellationToken cancellationToken) =>
        store.ListAsync(centralCompanyId, page, pageSize, cancellationToken);

    public async Task<CentralBranchConnectionView> RegisterAsync(Guid centralCompanyId,
        Guid actorId, CentralBranchConnectionCreate request, CancellationToken cancellationToken)
    {
        if (centralCompanyId == Guid.Empty || actorId == Guid.Empty
            || request.Token is not { Length: 64 } || !request.Token.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid branch connection request.");

        var installation = CentralInstallation.Create(request.InstallationId,
            centralCompanyId, request.RemoteCompanyId, request.RemoteBranchId,
            request.Label, request.DestinationUrl,
            CentralManagedSecretReference.Create(request.InstallationId));

        CentralApprovedDestination destination;
        try
        {
            destination = await destinationPolicy.ApproveForCompanyAsync(
                installation.DestinationUrl, centralCompanyId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await RecordRejectedAsync(centralCompanyId, actorId, cancellationToken);
            throw new CentralBranchConnectionVerificationException();
        }

        var encrypted = cryptoFactory().Encrypt(centralCompanyId,
            installation.InstallationId, request.Token);
        try
        {
            await verifier.VerifyAsync(destination, request.Token,
                installation.InstallationId, installation.RemoteCompanyId,
                installation.RemoteBranchId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await RecordRejectedAsync(centralCompanyId, actorId, cancellationToken);
            throw new CentralBranchConnectionVerificationException();
        }

        var credential = CentralBranchCredential.Create(installation.InstallationId, encrypted);
        var audit = CentralAuditEvent.Record(CentralAuditAction.InstallationRegistrationAccepted,
            CentralAuditResult.Succeeded, centralCompanyId, actorId, clock.GetUtcNow());
        await store.SaveRegisteredAsync(installation, credential, audit, cancellationToken);
        return new CentralBranchConnectionView(installation.InstallationId,
            installation.RemoteCompanyId, installation.RemoteBranchId,
            installation.Label, installation.DestinationUrl, installation.IsActive, true);
    }

    private Task RecordRejectedAsync(Guid companyId, Guid actorId,
        CancellationToken cancellationToken) => store.RecordRejectedAsync(
            CentralAuditEvent.Record(CentralAuditAction.InstallationRegistrationRejected,
                CentralAuditResult.Denied, companyId, actorId, clock.GetUtcNow()),
            cancellationToken);
}
