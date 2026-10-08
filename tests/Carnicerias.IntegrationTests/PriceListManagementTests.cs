using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class PriceListManagementTests
{
    [Fact]
    public void ListCanBeRenamedAndInactivatedWithoutChangingIdentity()
    {
        var list = new PriceList(Guid.NewGuid(), "Mostrador");
        var id = list.Id;

        list.Rename("  Convenio  ");
        list.Deactivate();

        Assert.Equal(id, list.Id);
        Assert.Equal("Convenio", list.Name);
        Assert.False(list.IsActive);
        list.Activate();
        Assert.True(list.IsActive);
    }

    [Fact]
    public void BranchAssignmentCanBeChangedWithoutDeletingIt()
    {
        var assignment = new BranchPriceList(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var branchId = assignment.BranchId;

        assignment.Deactivate();
        Assert.False(assignment.IsActive);
        assignment.Activate();

        Assert.True(assignment.IsActive);
        Assert.Equal(branchId, assignment.BranchId);
    }

    [Fact]
    public void ListNameCannotExceedStoredColumn()
    {
        Assert.Throws<ArgumentException>(() => new PriceList(Guid.NewGuid(), new string('a', 161)));
    }
}
