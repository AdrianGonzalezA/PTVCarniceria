namespace Carnicerias.Domain.PlatformAccess;

public sealed record OperationalContext(
    Guid UserId,
    Guid CompanyId,
    Guid BranchId,
    IReadOnlySet<string> Permissions,
    Guid SessionId);
