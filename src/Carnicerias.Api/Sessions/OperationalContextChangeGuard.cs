using Carnicerias.Domain.PlatformAccess;
using Microsoft.Extensions.Logging;

namespace Carnicerias.Api.Sessions;

public sealed record OperationalContextChangeCheck(
    bool IsAvailable,
    IReadOnlyList<OperationalContextChangeBlock> Blocks);

public sealed partial class OperationalContextChangeGuard(
    IEnumerable<IOperationalContextChangeBlocker> blockers,
    ILogger<OperationalContextChangeGuard> logger)
{
    public async Task<OperationalContextChangeCheck> CheckAsync(
        OperationalContext context,
        CancellationToken cancellationToken = default)
    {
        var results = new List<OperationalContextChangeBlock>();

        foreach (var blocker in blockers)
        {
            try
            {
                var reported = await blocker.GetBlocksAsync(context, cancellationToken);
                if (reported is null || reported.Any(block =>
                        string.IsNullOrWhiteSpace(block.Code) || block.Code.Length > 80 ||
                        string.IsNullOrWhiteSpace(block.Message) || block.Message.Length > 240))
                {
                    LogInvalidResult(logger);
                    return new OperationalContextChangeCheck(false, []);
                }

                results.AddRange(reported);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogBlockerFailure(logger, exception);
                return new OperationalContextChangeCheck(false, []);
            }
        }

        return new OperationalContextChangeCheck(
            true,
            results.Distinct().OrderBy(block => block.Code, StringComparer.Ordinal).ToArray());
    }

    [LoggerMessage(EventId = 7101, Level = LogLevel.Error, Message = "Operational context blocker returned an invalid result")]
    private static partial void LogInvalidResult(ILogger logger);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Error, Message = "Operational context blocker failed")]
    private static partial void LogBlockerFailure(ILogger logger, Exception exception);
}
