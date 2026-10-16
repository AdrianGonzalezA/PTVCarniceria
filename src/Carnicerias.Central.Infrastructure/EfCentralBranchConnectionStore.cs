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

    public static IQueryable<CentralInstallation> ActiveRemotePair(
        IQueryable<CentralInstallation> installations, Guid remoteCompanyId,
        Guid remoteBranchId, Guid? excludingInstallationId = null) =>
        installations.Where(item => item.IsActive
            && item.RemoteCompanyId == remoteCompanyId
            && item.RemoteBranchId == remoteBranchId
            && (excludingInstallationId == null
                || item.InstallationId != excludingInstallationId));
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
                : HasLegacyCredential(item),
            item.LastCheckStatus, item.LastCheckedAtUtc))
            .ToArray();
        return new CentralManagementPage<CentralBranchConnectionView>(
            data, page, pageSize, count);
    }

    public async Task<CentralBranchConnectionSnapshot?> FindAsync(Guid centralCompanyId,
        Guid installationId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var installation = await context.Installations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CentralCompanyId == centralCompanyId
            && item.InstallationId == installationId, cancellationToken);
        if (installation is null) return null;
        var hasCredential = CentralManagedSecretReference.IsManaged(installation)
            ? await context.BranchCredentials.AsNoTracking().AnyAsync(item =>
                item.InstallationId == installationId, cancellationToken)
            : HasLegacyCredential(installation);
        return new CentralBranchConnectionSnapshot(installation, hasCredential);
    }

    public async Task SaveRegisteredAsync(CentralInstallation installation,
        CentralBranchCredential credential, CentralAuditEvent audit,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var duplicate = await context.Installations.AsNoTracking().AnyAsync(item =>
            item.InstallationId == installation.InstallationId, cancellationToken)
            || await CentralBranchConnectionQuery.ActiveRemotePair(
                context.Installations.AsNoTracking(), installation.RemoteCompanyId,
                installation.RemoteBranchId).AnyAsync(cancellationToken);
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

    public async Task SaveChangedAsync(CentralInstallation installation,
        CentralBranchCredential? credential, CentralAuditEvent audit,
        long expectedRevision, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (audit.Action == CentralAuditAction.InstallationReactivated
            && await CentralBranchConnectionQuery.ActiveRemotePair(
                context.Installations.AsNoTracking(), installation.RemoteCompanyId,
                installation.RemoteBranchId, installation.InstallationId)
                .AnyAsync(cancellationToken))
            throw new CentralBranchConnectionConflictException();
        context.Installations.Update(installation);
        context.Entry(installation).Property(item => item.Revision).OriginalValue = expectedRevision;
        if (credential is not null)
        {
            var old = await context.BranchCredentials.SingleOrDefaultAsync(item =>
                item.InstallationId == installation.InstallationId, cancellationToken);
            if (old is null) context.BranchCredentials.Add(credential);
            else old.ReplaceWith(credential);
        }
        context.AuditEvents.Add(audit);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        { throw new CentralBranchConnectionConflictException(); }
        catch (DbUpdateException exception) when (IsConflict(exception.InnerException))
        { throw new CentralBranchConnectionConflictException(); }
        catch (PostgresException exception) when (IsConflict(exception))
        { throw new CentralBranchConnectionConflictException(); }
    }

    private static bool IsConflict(Exception? exception) => exception is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure
    };

    private static bool HasLegacyCredential(CentralInstallation installation) =>
        Environment.GetEnvironmentVariable(
            "CARNICERIAS_CENTRAL_BRANCH_SECRET_" + installation.SecretReference)
            is { Length: 64 } token && token.All(Uri.IsHexDigit);
}
