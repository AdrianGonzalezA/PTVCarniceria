using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Carnicerias.Central.Infrastructure;

public interface ICentralBusinessInstallationReader
{
    Task<IReadOnlyList<CentralInstallation>> ReadActiveAsync(Guid centralCompanyId,
        CancellationToken cancellationToken);
}

public sealed class EfCentralBusinessInstallationReader(CentralAccessDbContext database)
    : ICentralBusinessInstallationReader
{
    public async Task<IReadOnlyList<CentralInstallation>> ReadActiveAsync(Guid centralCompanyId,
        CancellationToken cancellationToken) =>
        await CentralInstallationQuery.Scope(database.Installations.AsNoTracking(), centralCompanyId)
            .Take(101).ToArrayAsync(cancellationToken);
}

public interface ICentralBranchSummarySource
{
    Task<CentralBranchSummary> ReadAsync(CentralInstallation installation,
        CentralBusinessWindow window, CancellationToken cancellationToken);
}

public interface ICentralBranchCredentialSource
{
    string? Read(string reference);
}

public sealed class EnvironmentCentralBranchCredentialSource : ICentralBranchCredentialSource
{
    public string? Read(string reference) => Environment.GetEnvironmentVariable(
        "CARNICERIAS_CENTRAL_BRANCH_SECRET_" + reference);
}

public sealed class CentralLiveBranchSummarySource(
    CentralDestinationPolicy destinationPolicy, ICentralBranchCredentialSource credentials)
    : ICentralBranchSummarySource
{
    public async Task<CentralBranchSummary> ReadAsync(CentralInstallation installation,
        CentralBusinessWindow window, CancellationToken cancellationToken)
    {
        var secret = credentials.Read(installation.SecretReference);
        if (secret is null || secret.Length != 64 || !secret.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Branch credential is unavailable.");
        var approved = await destinationPolicy.ApproveAsync(
            installation.DestinationUrl, cancellationToken);
        return await CentralBranchSummaryProbe.ReadAsync(approved, secret, installation,
            window, cancellationToken);
    }
}

public sealed record CentralBusinessAggregate(string Currency, int SaleCount,
    decimal SalesTotal, decimal SalePaymentsTotal);

public sealed record CentralBusinessBranchRow(Guid InstallationId, string Label, string Status,
    DateTimeOffset? GeneratedAtUtc, int? SaleCount, decimal? SalesTotal,
    decimal? SalePaymentsTotal, int? OpenShiftCount, decimal? OpenShiftCashBalance,
    IReadOnlyList<CentralPaymentSummary>? PaymentsByMethod)
{
    public static CentralBusinessBranchRow Available(CentralInstallation installation,
        CentralBranchSummary summary) => new(installation.InstallationId, installation.Label,
            "ok", summary.GeneratedAtUtc, summary.SaleCount, summary.SalesTotal,
            summary.SalePaymentsTotal, summary.OpenShiftCount, summary.OpenShiftCashBalance,
            summary.PaymentsByMethod);

    public static CentralBusinessBranchRow Unavailable(CentralInstallation installation) =>
        new(installation.InstallationId, installation.Label, "unavailable",
            null, null, null, null, null, null, null);
}

public sealed record CentralBusinessSummaryResponse(CentralBusinessWindow Window,
    DateTimeOffset RetrievedAtUtc, string Completeness, CentralBusinessAggregate? Aggregate,
    IReadOnlyList<CentralBusinessBranchRow> Branches);

public sealed class CentralBusinessSummaryService(
    ICentralBusinessInstallationReader reader,
    ICentralBranchSummarySource source,
    TimeProvider clock,
    ILogger<CentralBusinessSummaryService> logger)
{
    private static readonly Action<ILogger, Guid, string, Exception?> LogBranchUnavailable =
        LoggerMessage.Define<Guid, string>(LogLevel.Warning,
            new EventId(4201, "CentralBranchSummaryUnavailable"),
            "Central branch summary unavailable for installation {InstallationId}: {FailureType}");
    private static readonly Action<ILogger, Guid, Exception?> LogDuplicateBranch =
        LoggerMessage.Define<Guid>(LogLevel.Warning,
            new EventId(4202, "CentralDuplicateRemoteBranch"),
            "Central installation {InstallationId} duplicates a remote branch; summary suppressed.");

    public async Task<CentralBusinessSummaryResponse> ReadAsync(Guid centralCompanyId,
        CentralBusinessWindow window, CancellationToken cancellationToken)
    {
        var installations = await reader.ReadActiveAsync(centralCompanyId, cancellationToken);
        if (installations.Count > 100)
            throw new InvalidOperationException("Too many active central installations.");
        if (installations.Count == 0)
            return new CentralBusinessSummaryResponse(window, clock.GetUtcNow(),
                "empty", null, []);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var concurrency = new SemaphoreSlim(4);
        var ordered = installations.OrderBy(installation => installation.Label)
            .ThenBy(installation => installation.InstallationId).ToArray();
        var duplicateIds = ordered.GroupBy(installation =>
                (installation.RemoteCompanyId, installation.RemoteBranchId))
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .Select(installation => installation.InstallationId)
            .ToHashSet();
        var rows = await Task.WhenAll(ordered.Select(ReadBranchAsync));
        var complete = rows.All(row => row.Status == "ok");
        var aggregate = complete ? new CentralBusinessAggregate("ARS",
            checked(rows.Sum(row => row.SaleCount!.Value)),
            rows.Sum(row => row.SalesTotal!.Value),
            rows.Sum(row => row.SalePaymentsTotal!.Value)) : null;
        return new CentralBusinessSummaryResponse(window, clock.GetUtcNow(),
            complete ? "complete" : "incomplete", aggregate, rows);

        async Task<CentralBusinessBranchRow> ReadBranchAsync(CentralInstallation installation)
        {
            if (duplicateIds.Contains(installation.InstallationId))
            {
                LogDuplicateBranch(logger, installation.InstallationId, null);
                return CentralBusinessBranchRow.Unavailable(installation);
            }

            var entered = false;
            try
            {
                await concurrency.WaitAsync(deadline.Token);
                entered = true;
                var summary = await source.ReadAsync(installation, window, deadline.Token);
                return CentralBusinessBranchRow.Available(installation, summary);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogBranchUnavailable(logger, installation.InstallationId,
                    exception.GetType().Name, null);
                return CentralBusinessBranchRow.Unavailable(installation);
            }
            finally
            {
                if (entered) concurrency.Release();
            }
        }
    }
}
