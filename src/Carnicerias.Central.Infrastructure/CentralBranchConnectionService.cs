namespace Carnicerias.Central.Infrastructure;

public sealed record CentralBranchConnectionCreate(Guid InstallationId, Guid RemoteCompanyId,
    Guid RemoteBranchId, string Label, string DestinationUrl, string Token);
public sealed record CentralBranchConnectionEdit(string? Label, string? DestinationUrl,
    string? Token);

public sealed record CentralBranchConnectionView(Guid InstallationId, Guid RemoteCompanyId,
    Guid RemoteBranchId, string Label, string DestinationUrl, bool IsActive,
    bool HasCredential, string LastCheckStatus = "notChecked",
    DateTimeOffset? LastCheckedAtUtc = null);
public sealed record CentralBranchConnectionSnapshot(CentralInstallation Installation,
    bool HasCredential);

public sealed class CentralBranchConnectionVerificationException : Exception;
public sealed class CentralBranchConnectionConflictException : Exception;
public sealed class CentralBranchConnectionNotFoundException : Exception;

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
    Task<CentralBranchConnectionSnapshot?> FindAsync(Guid centralCompanyId, Guid installationId,
        CancellationToken cancellationToken);
    Task SaveRegisteredAsync(CentralInstallation installation,
        CentralBranchCredential credential, CentralAuditEvent audit,
        CancellationToken cancellationToken);
    Task SaveChangedAsync(CentralInstallation installation,
        CentralBranchCredential? credential, CentralAuditEvent audit,
        long expectedRevision, CancellationToken cancellationToken);
    Task RecordRejectedAsync(CentralAuditEvent audit, CancellationToken cancellationToken);
}

public sealed class CentralBranchConnectionService(
    ICentralBranchConnectionStore store,
    ICentralInstallationVerifier verifier,
    CentralDestinationPolicy destinationPolicy,
    ICentralBranchCredentialResolver credentials,
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
            await RecordRejectedAsync(centralCompanyId, actorId,
                installation.InstallationId, cancellationToken);
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
            await RecordRejectedAsync(centralCompanyId, actorId,
                installation.InstallationId, cancellationToken);
            throw new CentralBranchConnectionVerificationException();
        }

        var credential = CentralBranchCredential.Create(installation.InstallationId, encrypted);
        var audit = CentralAuditEvent.Record(CentralAuditAction.InstallationRegistrationAccepted,
            CentralAuditResult.Succeeded, centralCompanyId, actorId, clock.GetUtcNow(),
            installation.InstallationId);
        await store.SaveRegisteredAsync(installation, credential, audit, cancellationToken);
        return View(installation, true);
    }

    public async Task<CentralBranchConnectionView> CheckAsync(Guid centralCompanyId,
        Guid actorId, Guid installationId, CancellationToken cancellationToken)
    {
        var snapshot = await FindAsync(centralCompanyId, installationId, cancellationToken);
        var installation = snapshot.Installation;
        var revision = installation.Revision;
        var available = await VerifyExistingAsync(installation, cancellationToken);
        var now = clock.GetUtcNow();
        installation.RecordCheck(available, now);
        await store.SaveChangedAsync(installation, null, Audit(
            CentralAuditAction.InstallationChecked,
            available ? CentralAuditResult.Succeeded : CentralAuditResult.Failed,
            centralCompanyId, actorId, installationId, now), revision, cancellationToken);
        return View(installation, snapshot.HasCredential);
    }

    public async Task<CentralBranchConnectionView> EditAsync(Guid centralCompanyId,
        Guid actorId, Guid installationId, CentralBranchConnectionEdit request,
        CancellationToken cancellationToken)
    {
        var snapshot = await FindAsync(centralCompanyId, installationId, cancellationToken);
        var installation = snapshot.Installation;
        if (request.Label is null && request.DestinationUrl is null && request.Token is null)
            throw new ArgumentException("No fields to edit.");
        if (request.Token is not null && (request.Token.Length != 64
            || !request.Token.All(Uri.IsHexDigit)))
            throw new ArgumentException("Invalid branch token.");

        var label = request.Label ?? installation.Label;
        var url = request.DestinationUrl ?? installation.DestinationUrl;
        var changingConnection = request.DestinationUrl is not null || request.Token is not null;
        // Validate the entire proposed configuration before any network call or mutation.
        var reference = request.Token is null ? installation.SecretReference
            : CentralManagedSecretReference.Create(installation.InstallationId);
        _ = CentralInstallation.Create(installation.InstallationId,
            installation.CentralCompanyId, installation.RemoteCompanyId,
            installation.RemoteBranchId, label, url, reference);

        CentralBranchCredential? credential = null;
        if (changingConnection)
        {
            try
            {
                var destination = await destinationPolicy.ApproveForCompanyAsync(url,
                    centralCompanyId, cancellationToken);
                var token = request.Token ?? await credentials.ResolveAsync(installation,
                    cancellationToken);
                await verifier.VerifyAsync(destination, token, installation.InstallationId,
                    installation.RemoteCompanyId, installation.RemoteBranchId,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                await store.RecordRejectedAsync(Audit(
                    CentralAuditAction.InstallationEditRejected, CentralAuditResult.Denied,
                    centralCompanyId, actorId, installationId, clock.GetUtcNow()),
                    cancellationToken);
                throw new CentralBranchConnectionVerificationException();
            }
            if (request.Token is not null)
                credential = CentralBranchCredential.Create(installation.InstallationId,
                    cryptoFactory().Encrypt(centralCompanyId,
                        installation.InstallationId, request.Token));
        }

        var revision = installation.Revision;
        var now = clock.GetUtcNow();
        installation.UpdateConfiguration(label, url, reference, changingConnection, now);
        await store.SaveChangedAsync(installation, credential, Audit(
            CentralAuditAction.InstallationEdited, CentralAuditResult.Succeeded,
            centralCompanyId, actorId, installationId, now), revision, cancellationToken);
        return View(installation, snapshot.HasCredential || credential is not null);
    }

    public async Task<CentralBranchConnectionView> ChangeStateAsync(Guid centralCompanyId,
        Guid actorId, Guid installationId, bool isActive,
        CancellationToken cancellationToken)
    {
        var snapshot = await FindAsync(centralCompanyId, installationId, cancellationToken);
        var installation = snapshot.Installation;
        if (installation.IsActive == isActive) return View(installation, snapshot.HasCredential);
        var revision = installation.Revision;
        var now = clock.GetUtcNow();
        if (isActive)
        {
            if (!await VerifyExistingAsync(installation, cancellationToken))
            {
                await store.RecordRejectedAsync(Audit(
                    CentralAuditAction.InstallationReactivationRejected, CentralAuditResult.Denied,
                    centralCompanyId, actorId, installationId, now), cancellationToken);
                throw new CentralBranchConnectionVerificationException();
            }
            installation.RecordCheck(true, now);
        }
        installation.ChangeState(isActive);
        await store.SaveChangedAsync(installation, null, Audit(
            isActive ? CentralAuditAction.InstallationReactivated
                : CentralAuditAction.InstallationDeactivated,
            CentralAuditResult.Succeeded, centralCompanyId, actorId, installationId, now),
            revision, cancellationToken);
        return View(installation, snapshot.HasCredential);
    }

    private async Task<bool> VerifyExistingAsync(CentralInstallation installation,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = CentralManagedSecretReference.IsManaged(installation)
                ? await destinationPolicy.ApproveForCompanyAsync(
                    installation.DestinationUrl, installation.CentralCompanyId,
                    cancellationToken)
                : await destinationPolicy.ApproveAsync(installation.DestinationUrl,
                    cancellationToken);
            var token = await credentials.ResolveAsync(installation, cancellationToken);
            await verifier.VerifyAsync(destination, token, installation.InstallationId,
                installation.RemoteCompanyId, installation.RemoteBranchId,
                cancellationToken);
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    private async Task<CentralBranchConnectionSnapshot> FindAsync(Guid centralCompanyId,
        Guid installationId, CancellationToken cancellationToken)
    {
        if (centralCompanyId == Guid.Empty || installationId == Guid.Empty)
            throw new CentralBranchConnectionNotFoundException();
        return await store.FindAsync(centralCompanyId, installationId, cancellationToken)
            ?? throw new CentralBranchConnectionNotFoundException();
    }

    private static CentralBranchConnectionView View(CentralInstallation installation,
        bool hasCredential) => new(installation.InstallationId,
            installation.RemoteCompanyId, installation.RemoteBranchId,
            installation.Label, installation.DestinationUrl, installation.IsActive,
            hasCredential, installation.LastCheckStatus, installation.LastCheckedAtUtc);

    private static CentralAuditEvent Audit(CentralAuditAction action,
        CentralAuditResult result, Guid companyId, Guid actorId,
        Guid installationId, DateTimeOffset now) =>
        CentralAuditEvent.Record(action, result, companyId, actorId, now, installationId);

    private Task RecordRejectedAsync(Guid companyId, Guid actorId, Guid installationId,
        CancellationToken cancellationToken) => store.RecordRejectedAsync(
            CentralAuditEvent.Record(CentralAuditAction.InstallationRegistrationRejected,
                CentralAuditResult.Denied, companyId, actorId, clock.GetUtcNow(), installationId),
            cancellationToken);
}
