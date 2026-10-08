using Carnicerias.PlatformAccess;

namespace Carnicerias.IntegrationTests;

public sealed class PlatformPermissionCatalogTests
{
    [Fact]
    public void DefaultCatalogContainsAllPlatformAdministrationPermissions()
    {
        var permissions = PlatformPermissionCatalog.CreateDefaultPermissions();

        Assert.Equal(
            ["catalog.manage", "inventory.stock.manage", "organization.manage",
                "platform.assignments.manage", "platform.roles.manage", "platform.users.manage"],
            permissions.Select(permission => permission.Code).Order(StringComparer.Ordinal));
    }
}
