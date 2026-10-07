using Carnicerias.Domain.PlatformAccess;

namespace Carnicerias.Api.Security;

public sealed class OperationalContextAccessor
{
    private OperationalContext? _context;

    public OperationalContext Context => _context
        ?? throw new InvalidOperationException("This endpoint has no authorized operational context.");

    internal void Set(OperationalContext context) => _context = context;
}
