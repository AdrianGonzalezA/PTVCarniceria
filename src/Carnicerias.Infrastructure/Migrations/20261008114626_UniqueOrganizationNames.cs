using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueOrganizationNames : Migration
    {
        private static readonly string[] CompanyNameColumns = ["CompanyId", "Name"];
        private const string PermissionId = "f9b6285e-2ad5-44e1-bcb4-856af7e9dd7a";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_companies_Name",
                schema: "platform_access",
                table: "companies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branches_CompanyId_Name",
                schema: "platform_access",
                table: "branches",
                columns: CompanyNameColumns,
                unique: true);

            migrationBuilder.Sql($$"""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES ('{{PermissionId}}', 'organization.manage', 'Administrar empresas, sucursales y terminales')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" = 'administrator' AND permission."Code" = 'organization.manage'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DELETE FROM platform_access.role_permissions WHERE "PermissionId" = '{{PermissionId}}';
                DELETE FROM platform_access.permissions WHERE "Id" = '{{PermissionId}}';
                """);

            migrationBuilder.DropIndex(
                name: "IX_companies_Name",
                schema: "platform_access",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "IX_branches_CompanyId_Name",
                schema: "platform_access",
                table: "branches");
        }
    }
}
