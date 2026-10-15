using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Bootstrap;

public sealed record CentralRegistrationRequest(
    string CompanyCode, Guid InstallationId, Guid RemoteCompanyId,
    Guid RemoteBranchId, string Label, string DestinationUrl, string SecretReference);

public interface ICentralRegistrationStore
{
    Task<CentralCompany?> FindCompanyAsync(string code, CancellationToken cancellationToken);
    Task<bool> InstallationExistsAsync(Guid installationId, CancellationToken cancellationToken);
    Task SaveAcceptedAsync(CentralInstallation installation, CentralAuditEvent audit,
        CancellationToken cancellationToken);
    Task SaveRejectedAsync(CentralAuditEvent audit, CancellationToken cancellationToken);
}

public interface ICentralBranchSecretSource
{
    string? Read(string reference);
}

public interface ICentralBranchProbe
{
    Task VerifyAsync(CentralApprovedDestination approved, string secret,
        Guid installationId, Guid companyId, Guid branchId,
        CancellationToken cancellationToken);
}

public sealed class PinnedCentralBranchProbe : ICentralBranchProbe
{
    public async Task VerifyAsync(CentralApprovedDestination approved, string secret,
        Guid installationId, Guid companyId, Guid branchId,
        CancellationToken cancellationToken) =>
        _ = await CentralInstallationProbe.VerifyAsync(approved, secret,
            installationId, companyId, branchId, cancellationToken);
}

public sealed class CentralRegistrationService(
    ICentralRegistrationStore store,
    ICentralBranchSecretSource secretSource,
    ICentralBranchProbe probe,
    CentralDestinationPolicy destinationPolicy,
    TimeProvider clock)
{
    public async Task<Guid> RegisterAsync(CentralRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        Guid? centralCompanyId = null;
        try
        {
            var code = (request.CompanyCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length is < 2 or > 40 || code.Any(character =>
                    !char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character)
                    && character != '-'))
            {
                throw new ArgumentException("Invalid central company code.");
            }

            var company = await store.FindCompanyAsync(code, cancellationToken);
            if (company is null || !company.IsActive)
            {
                throw new InvalidOperationException("Central company is unavailable.");
            }

            centralCompanyId = company.Id;
            var installation = CentralInstallation.Create(request.InstallationId,
                company.Id, request.RemoteCompanyId, request.RemoteBranchId,
                request.Label, request.DestinationUrl, request.SecretReference);
            if (await store.InstallationExistsAsync(installation.InstallationId, cancellationToken))
            {
                throw new InvalidOperationException("Installation already registered.");
            }

            var approved = await destinationPolicy.ApproveAsync(
                installation.DestinationUrl, cancellationToken);
            var secret = secretSource.Read(installation.SecretReference);
            if (secret is null || secret.Length != 64 || !secret.All(Uri.IsHexDigit))
            {
                throw new InvalidOperationException("Branch credential is unavailable.");
            }

            await probe.VerifyAsync(approved, secret, installation.InstallationId,
                installation.RemoteCompanyId, installation.RemoteBranchId, cancellationToken);
            var audit = CentralAuditEvent.Record(CentralAuditAction.InstallationRegistrationAccepted,
                CentralAuditResult.Succeeded, company.Id, null, clock.GetUtcNow());
            await store.SaveAcceptedAsync(installation, audit, cancellationToken);
            return installation.InstallationId;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            var audit = CentralAuditEvent.Record(CentralAuditAction.InstallationRegistrationRejected,
                CentralAuditResult.Denied, centralCompanyId, null, clock.GetUtcNow());
            await store.SaveRejectedAsync(audit, cancellationToken);
            throw new InvalidOperationException("Central installation registration refused.");
        }
    }
}
