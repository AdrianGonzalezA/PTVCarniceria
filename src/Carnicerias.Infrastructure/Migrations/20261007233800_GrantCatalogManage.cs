using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Carnicerias.Infrastructure.Migrations;

[DbContext(typeof(PlatformAccessDbContext))]
[Migration("20261007233800_GrantCatalogManage")]
public sealed class GrantCatalogManage : Migration
{
    private const string PermissionId = "c6a6fa56-2d9d-469b-a13d-1a23683a783e";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($$"""
            INSERT INTO platform_access.permissions ("Id", "Code", "Description")
            VALUES ('{{PermissionId}}', 'catalog.manage', 'Administrar el catálogo de productos y precios')
            ON CONFLICT ("Code") DO NOTHING;

            INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
            SELECT role."Id", permission."Id"
            FROM platform_access.roles AS role
            CROSS JOIN platform_access.permissions AS permission
            WHERE role."Code" = 'administrator' AND permission."Code" = 'catalog.manage'
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($$"""
            DELETE FROM platform_access.role_permissions
            WHERE "PermissionId" = '{{PermissionId}}';
            DELETE FROM platform_access.permissions
            WHERE "Id" = '{{PermissionId}}';
            """);
    }
}
