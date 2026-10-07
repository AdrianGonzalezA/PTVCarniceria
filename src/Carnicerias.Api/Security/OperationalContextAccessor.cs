using Carnicerias.Domain.PlatformAccess;

namespace Carnicerias.Api.Security;

public sealed class OperationalContextAccessor
{
    private OperationalContext? _context;
    private Guid? _terminalId;

    public OperationalContext Context => _context
        ?? throw new InvalidOperationException("This endpoint has no authorized operational context.");

    public Guid? TerminalId => _terminalId;

    internal void Set(OperationalContext context, Guid? terminalId)
    {
        _context = context;
        _terminalId = terminalId;
    }
}
