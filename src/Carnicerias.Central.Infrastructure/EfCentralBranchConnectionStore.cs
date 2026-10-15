using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Central.Infrastructure;

public static class CentralBranchConnectionQuery
{
    public static IOrderedQueryable<CentralInstallation> Scope(
        IQueryable<CentralInstallation> installations, Guid centralCompanyId) =>
        installations.Where(item => item.CentralCompanyId == centralCompanyId)
            .OrderBy(item => item.Label).ThenBy(item => item.InstallationId);
}

public sealed class EfCentralBranchConnectionStore(
    IDbContextFactory<CentralAccessDbContext> contextFactory) : ICentralBranchConnectionStore
{
    public async Task<CentralManagementPage<CentralBranchConnectionView>> ListAsync(
        Guid centralCompanyId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var scope = CentralBranchConnectionQuery.Scope(context.Installations.AsNoTracking(),
            centralCompanyId);
        var count = await scope.CountAsync(cancellationToken);
        var installations = await scope.Skip((page - 1) * pageSize).Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var ids = installations.Select(item => item.InstallationId).ToArray();
        var managedIds = (await context.BranchCredentials.AsNoTracking()
            .Where(item => ids.Contains(item.InstallationId))
            .Select(item => item.InstallationId).ToArrayAsync(cancellationToken)).ToHashSet();
        var data = installations.Select(item => new CentralBranchConnectionView(
            item.InstallationId, item.RemoteCompanyId, item.RemoteBranchId,
            item.Label, item.DestinationUrl, item.IsActive,
            CentralManagedSecretReference.IsManaged(item)
                ? managedIds.Contains(item.InstallationId)
                : Environment.GetEnvironmentVariable(
                    "CARNICERIAS_CENTRAL_BRANCH_SECRET_" + item.SecretReference) is { Length: 64 }))
            .ToArray();
        return new CentralManagementPage<CentralBranchConnectionView>(
            data, page, pageSize, count);
    }

    public async Task SaveRegisteredAsync(CentralInstallation installation,
        CentralBranchCredential credential, CentralAuditEvent audit,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var duplicate = await context.Installations.AsNoTracking().AnyAsync(item =>
            item.InstallationId == installation.InstallationId
            || item.IsActive && item.CentralCompanyId == installation.CentralCompanyId
                && item.RemoteCompanyId == installation.RemoteCompanyId
                && item.RemoteBranchId == installation.RemoteBranchId, cancellationToken);
        if (duplicate) throw new CentralBranchConnectionConflictException();
        context.Installations.Add(installation);
        context.BranchCredentials.Add(credential);
        context.AuditEvents.Add(audit);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsConflict(exception.InnerException))
        { throw new CentralBranchConnectionConflictException(); }
        catch (PostgresException exception) when (IsConflict(exception))
        { throw new CentralBranchConnectionConflictException(); }
    }

    public async Task RecordRejectedAsync(CentralAuditEvent audit,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.AuditEvents.Add(audit);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsConflict(Exception? exception) => exception is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure
    };
}
