namespace Carnicerias.Domain.PlatformAccess;

/// <summary>
/// Reports open operations that must be resolved before a session can leave its current context.
/// Messages must be safe, concise, and must not include credentials or personal data.
/// </summary>
public interface IOperationalContextChangeBlocker
{
    Task<IReadOnlyList<OperationalContextChangeBlock>> GetBlocksAsync(
        OperationalContext context,
        CancellationToken cancellationToken = default);
}
