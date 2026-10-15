using Carnicerias.PlatformAccess;

namespace Carnicerias.ArchitectureTests;

public sealed class BranchCentralConnectionEventTests
{
    [Fact]
    public void AuditEventRecordsActorAndActionWithoutCredentialMaterial()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var atUtc = new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);

        var audit = BranchCentralConnectionEvent.Create(companyId, branchId,
            actorId, BranchCentralConnectionAction.CredentialIssued, atUtc);

        Assert.Equal(companyId, audit.CompanyId);
        Assert.Equal(branchId, audit.BranchId);
        Assert.Equal(actorId, audit.ActorUserId);
        Assert.Equal(BranchCentralConnectionAction.CredentialIssued, audit.Action);
        Assert.DoesNotContain(typeof(BranchCentralConnectionEvent).GetProperties(),
            property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }
}
