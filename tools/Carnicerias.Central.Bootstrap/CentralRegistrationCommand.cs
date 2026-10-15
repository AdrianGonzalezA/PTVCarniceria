using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Central.Bootstrap;

internal static class CentralRegistrationCommand
{
    public static async Task<Guid> RunAsync(string? connectionString,
        CentralRegistrationRequest request, bool allowLoopbackHttp,
        CancellationToken cancellationToken)
    {
        CentralConnectionGuard.Validate(connectionString);
        var approved = ParseApprovedEndpoints(
            Environment.GetEnvironmentVariable("CARNICERIAS_CENTRAL_APPROVED_BRANCHES"));
        await using var context = await CentralBootstrapCommand.OpenVerifiedContextAsync(
            connectionString, cancellationToken);
        var policy = new CentralDestinationPolicy(approved,
            allowLoopbackHttp, new CentralDnsResolver());
        var service = new CentralRegistrationService(new EfRegistrationStore(context),
            new EnvironmentBranchSecretSource(), new PinnedCentralBranchProbe(),
            policy, TimeProvider.System);
        return await service.RegisterAsync(request, cancellationToken);
    }

    internal static IReadOnlyList<CentralApprovedEndpoint> ParseApprovedEndpoints(string? value) =>
        CentralApprovedEndpointConfiguration.Parse(value);
}

internal sealed class EnvironmentBranchSecretSource : ICentralBranchSecretSource
{
    public string? Read(string reference) => Environment.GetEnvironmentVariable(
        "CARNICERIAS_CENTRAL_BRANCH_SECRET_" + reference);
}

internal sealed class EfRegistrationStore(CentralAccessDbContext database) : ICentralRegistrationStore
{
    public Task<CentralCompany?> FindCompanyAsync(string code, CancellationToken cancellationToken) =>
        database.Companies.AsNoTracking().SingleOrDefaultAsync(
            company => company.Code == code, cancellationToken);

    public Task<bool> InstallationExistsAsync(Guid installationId,
        CancellationToken cancellationToken) =>
        database.Installations.AnyAsync(installation =>
            installation.InstallationId == installationId, cancellationToken);

    public async Task SaveAcceptedAsync(CentralInstallation installation, CentralAuditEvent audit,
        CancellationToken cancellationToken)
    {
        database.Installations.Add(installation);
        database.AuditEvents.Add(audit);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
            throw new InvalidOperationException("Central installation already exists.");
        }
    }

    public async Task SaveRejectedAsync(CentralAuditEvent audit,
        CancellationToken cancellationToken)
    {
        database.AuditEvents.Add(audit);
        await database.SaveChangesAsync(cancellationToken);
    }
}
