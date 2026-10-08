using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;

namespace Carnicerias.IntegrationTests;

public sealed class ProductCategoryTests
{
    [Fact]
    public void RenameTrimsNameAndKeepsIdentity()
    {
        var category = new ProductCategory(Guid.NewGuid(), "Vacuno");
        var id = category.Id;

        category.Rename("  Cortes especiales  ");

        Assert.Equal(id, category.Id);
        Assert.Equal("Cortes especiales", category.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RenameRejectsEmptyNames(string name)
    {
        var category = new ProductCategory(Guid.NewGuid(), "Vacuno");

        Assert.Throws<ArgumentException>(() => category.Rename(name));
    }

    [Fact]
    public void CategoryCanBeInactivatedAndReactivatedWithoutChangingIdentity()
    {
        var category = new ProductCategory(Guid.NewGuid(), "Vacuno");
        var id = category.Id;

        category.Deactivate();
        Assert.False(category.IsActive);
        category.Activate();

        Assert.True(category.IsActive);
        Assert.Equal(id, category.Id);
    }

    [Fact]
    public void InitialAdministratorPermissionsIncludeCatalogManagement()
    {
        Assert.Contains(PlatformPermissionCatalog.CreateDefaultPermissions(),
            permission => permission.Code == PlatformPermissionCatalog.CatalogManage);
    }
}
