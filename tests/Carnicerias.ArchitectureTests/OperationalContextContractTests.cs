using Carnicerias.Domain.PlatformAccess;

namespace Carnicerias.ArchitectureTests;

public sealed class OperationalContextContractTests
{
    [Fact]
    public void ExposesTheAuthenticatedOperationalScope()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        IReadOnlySet<string> permissions = new HashSet<string>
        {
            "catalog.view",
            "sales.create",
        };

        var context = new OperationalContext(
            userId,
            companyId,
            branchId,
            permissions,
            sessionId);

        Assert.Equal(userId, context.UserId);
        Assert.Equal(companyId, context.CompanyId);
        Assert.Equal(branchId, context.BranchId);
        Assert.Equal(sessionId, context.SessionId);
        Assert.Same(permissions, context.Permissions);
    }
}
