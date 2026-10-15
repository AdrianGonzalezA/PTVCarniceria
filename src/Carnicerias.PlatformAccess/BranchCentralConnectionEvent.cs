namespace Carnicerias.PlatformAccess;

public enum BranchCentralConnectionAction
{
    UrlSaved,
    CredentialIssued,
    CredentialRotated,
    RotationCompleted,
    Deactivated
}

public sealed class BranchCentralConnectionEvent
{
    private BranchCentralConnectionEvent() { }

    private BranchCentralConnectionEvent(Guid companyId, Guid branchId, Guid actorUserId,
        BranchCentralConnectionAction action, DateTimeOffset atUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        ActorUserId = actorUserId;
        Action = action;
        AtUtc = atUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public BranchCentralConnectionAction Action { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }

    public static BranchCentralConnectionEvent Create(Guid companyId, Guid branchId,
        Guid actorUserId, BranchCentralConnectionAction action, DateTimeOffset atUtc)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || actorUserId == Guid.Empty ||
            !Enum.IsDefined(action) || atUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid branch connection audit event.");
        return new BranchCentralConnectionEvent(companyId, branchId, actorUserId, action, atUtc);
    }
}
