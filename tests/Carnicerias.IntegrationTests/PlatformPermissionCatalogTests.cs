using Carnicerias.PlatformAccess;

namespace Carnicerias.IntegrationTests;

public sealed class PlatformPermissionCatalogTests
{
    [Fact]
    public void DefaultCatalogContainsAllPlatformAdministrationPermissions()
    {
        var permissions = PlatformPermissionCatalog.CreateDefaultPermissions();

        Assert.Equal(
            ["cash.withdrawal.approve", "catalog.manage", "inventory.receipts.manage", "inventory.stock.manage", "inventory.transfers.manage", "organization.manage",
                "platform.assignments.manage", "platform.roles.manage", "platform.users.manage",
                "pos.account.charge", "pos.account.correct", "pos.discount.apply"],
            permissions.Select(permission => permission.Code).Order(StringComparer.Ordinal));
    }
}
