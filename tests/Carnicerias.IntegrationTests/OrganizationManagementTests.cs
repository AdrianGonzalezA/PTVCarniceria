using Carnicerias.PlatformAccess;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class OrganizationManagementTests
{
    [Fact]
    public void CompanyCanBeRenamedAndInactivatedWithoutChangingIdentity()
    {
        var company = new Company("Carnicerías del Sur");
        var id = company.Id;

        company.Rename("  Carnicerías del Centro  ");
        company.Deactivate();

        Assert.Equal(id, company.Id);
        Assert.Equal("Carnicerías del Centro", company.Name);
        Assert.False(company.IsActive);
        company.Activate();
        Assert.True(company.IsActive);
    }

    [Fact]
    public void BranchCanBeRenamedAndInactivatedWithoutChangingCompany()
    {
        var companyId = Guid.NewGuid();
        var branch = new Branch(companyId, "Centro");

        branch.Rename("  Norte  ");
        branch.Deactivate();

        Assert.Equal(companyId, branch.CompanyId);
        Assert.Equal("Norte", branch.Name);
        Assert.False(branch.IsActive);
        branch.Activate();
        Assert.True(branch.IsActive);
    }

    [Fact]
    public void NamesCannotExceedDatabaseColumns()
    {
        Assert.Throws<ArgumentException>(() => new Company(new string('a', 201)));
        Assert.Throws<ArgumentException>(() => new Branch(Guid.NewGuid(), new string('a', 201)));
    }

    [Fact]
    public void InitialAdministratorPermissionsIncludeOrganizationManagement()
    {
        Assert.Contains(PlatformPermissionCatalog.CreateDefaultPermissions(),
            permission => permission.Code == PlatformPermissionCatalog.OrganizationManage);
    }

    [Fact]
    public void TerminalCanBeRenamedAndReprovisionedWithoutChangingIdentity()
    {
        var terminal = new PosTerminal(Guid.NewGuid(), Guid.NewGuid(), "Caja 1");
        var id = terminal.Id;
        var credential = PosTerminalCredential.Issue();

        terminal.Rename("  Caja principal  ");
        terminal.Deactivate();
        terminal.ReactivateWithCredentialHash(credential.Hash);

        Assert.Equal(id, terminal.Id);
        Assert.Equal("Caja principal", terminal.Name);
        Assert.True(terminal.IsActive);
        Assert.Equal(credential.Hash, terminal.CredentialHash);
    }
}
