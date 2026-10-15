namespace Carnicerias.Central.Infrastructure;

public enum CentralAuditAction
{
    CompanyCreated = 0,
    AdministratorCreated = 1,
    PasswordReset = 2,
    SessionIssued = 3,
    SessionRevoked = 4,
    InstallationRegistrationAccepted = 5,
    InstallationRegistrationRejected = 6,
    SessionDenied = 7,
    ManufacturerCreated = 8,
    ManufacturerSessionIssued = 9,
    ManufacturerSessionRevoked = 10,
    ManufacturerSessionDenied = 11,
    ManufacturerPasswordReset = 12
}

public enum CentralAuditResult
{
    Succeeded = 0,
    Denied = 1,
    Failed = 2
}

public sealed class CentralAuditEvent
{
    private CentralAuditEvent() { }

    private CentralAuditEvent(CentralAuditAction action, CentralAuditResult result,
        Guid? centralCompanyId, Guid? actorId, DateTimeOffset occurredAtUtc)
    {
        Id = Guid.NewGuid();
        Action = action;
        Result = result;
        CentralCompanyId = centralCompanyId;
        ActorId = actorId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public CentralAuditAction Action { get; private set; }
    public CentralAuditResult Result { get; private set; }
    public Guid? CentralCompanyId { get; private set; }
    public Guid? ActorId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static CentralAuditEvent Record(CentralAuditAction action, CentralAuditResult result,
        Guid? centralCompanyId, Guid? actorId, DateTimeOffset occurredAtUtc)
    {
        if (!Enum.IsDefined(action) || !Enum.IsDefined(result))
            throw new ArgumentOutOfRangeException(nameof(action));
        if (occurredAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Audit timestamp must be UTC.", nameof(occurredAtUtc));
        return new CentralAuditEvent(action, result, centralCompanyId, actorId, occurredAtUtc);
    }
}
