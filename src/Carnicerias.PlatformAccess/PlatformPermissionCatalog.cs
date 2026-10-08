namespace Carnicerias.PlatformAccess;

public static class PlatformPermissionCatalog
{
    public const string UsersManage = "platform.users.manage";
    public const string RolesManage = "platform.roles.manage";
    public const string AssignmentsManage = "platform.assignments.manage";
    public const string InventoryStockManage = "inventory.stock.manage";
    public const string CatalogManage = "catalog.manage";
    public const string OrganizationManage = "organization.manage";

    public static IReadOnlyCollection<PermissionDefinition> CreateDefaultPermissions() =>
    [
        new PermissionDefinition(UsersManage, "Administrar usuarios de la plataforma"),
        new PermissionDefinition(RolesManage, "Administrar roles y permisos de la plataforma"),
        new PermissionDefinition(AssignmentsManage, "Administrar asignaciones de usuarios"),
        new PermissionDefinition(InventoryStockManage, "Administrar existencias de sucursal"),
        new PermissionDefinition(CatalogManage, "Administrar el catálogo de productos y precios"),
        new PermissionDefinition(OrganizationManage, "Administrar empresas, sucursales y terminales")
    ];
}
