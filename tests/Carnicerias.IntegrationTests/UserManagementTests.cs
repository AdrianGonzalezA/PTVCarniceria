using Carnicerias.PlatformAccess;

namespace Carnicerias.IntegrationTests;

public sealed class UserManagementTests
{
    [Fact]
    public void PasswordHashCanBeReplacedButNeverWithAnEmptyValue()
    {
        var user = UserIdentity.Create("cashier", "cashier@example.test", "initial-hash");

        user.SetPasswordHash("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.Throws<ArgumentException>(() => user.SetPasswordHash(" "));
        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void CashierAssignmentRequiresARealBranchIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new UserAssignment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
        var branchId = Guid.NewGuid();
        var assignment = new UserAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), branchId);
        Assert.Equal(branchId, assignment.BranchId);
    }
}
