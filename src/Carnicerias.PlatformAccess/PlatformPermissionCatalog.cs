namespace Carnicerias.PlatformAccess;

public static class PlatformPermissionCatalog
{
    public const string UsersManage = "platform.users.manage";
    public const string RolesManage = "platform.roles.manage";
    public const string AssignmentsManage = "platform.assignments.manage";
    public const string InventoryStockManage = "inventory.stock.manage";
    public const string CatalogManage = "catalog.manage";
    public const string OrganizationManage = "organization.manage";
    public const string PosAccountCharge = "pos.account.charge";
    public const string PosAccountCorrect = "pos.account.correct";

    public static IReadOnlyCollection<PermissionDefinition> CreateDefaultPermissions() =>
    [
        new PermissionDefinition(UsersManage, "Administrar usuarios de la plataforma"),
        new PermissionDefinition(RolesManage, "Administrar roles y permisos de la plataforma"),
        new PermissionDefinition(AssignmentsManage, "Administrar asignaciones de usuarios"),
        new PermissionDefinition(InventoryStockManage, "Administrar existencias de sucursal"),
        new PermissionDefinition(CatalogManage, "Administrar el catálogo de productos y precios"),
        new PermissionDefinition(OrganizationManage, "Administrar empresas, sucursales y terminales"),
        new PermissionDefinition(PosAccountCharge, "Cargar el saldo de una venta a la cuenta corriente de un cliente"),
        new PermissionDefinition(PosAccountCorrect, "Corregir cobranzas de cuenta corriente con constancia interna")
    ];
}
