using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Infrastructure;

public sealed record CentralInstallationListItem(
    Guid InstallationId, Guid RemoteCompanyId, Guid RemoteBranchId, string Label);

public sealed record CentralInstallationPage(
    IReadOnlyList<CentralInstallationListItem> Data, int Page, int PageSize, int TotalCount);

public interface ICentralInstallationReader
{
    Task<CentralInstallationPage> ReadAsync(Guid centralCompanyId, int page,
        int pageSize, CancellationToken cancellationToken);
}

public static class CentralInstallationQuery
{
    public static IOrderedQueryable<CentralInstallation> Scope(
        IQueryable<CentralInstallation> installations, Guid centralCompanyId) =>
        installations.Where(installation => installation.CentralCompanyId == centralCompanyId
                && installation.IsActive)
            .OrderBy(installation => installation.Label)
            .ThenBy(installation => installation.InstallationId);
}

public sealed class EfCentralInstallationReader(CentralAccessDbContext database)
    : ICentralInstallationReader
{
    public async Task<CentralInstallationPage> ReadAsync(Guid centralCompanyId, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        var scope = CentralInstallationQuery.Scope(database.Installations.AsNoTracking(),
            centralCompanyId);
        var total = await scope.CountAsync(cancellationToken);
        var data = await scope.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(installation => new CentralInstallationListItem(
                installation.InstallationId, installation.RemoteCompanyId,
                installation.RemoteBranchId, installation.Label))
            .ToArrayAsync(cancellationToken);
        return new CentralInstallationPage(data, page, pageSize, total);
    }
}
