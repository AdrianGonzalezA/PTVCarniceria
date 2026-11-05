using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Generated EF key and index arrays.

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyRolePermissionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_role_permission_overrides",
                schema: "platform_access",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsGranted = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_role_permission_overrides", x => new { x.CompanyId, x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_company_role_permission_overrides_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_role_permission_overrides_permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalSchema: "platform_access",
                        principalTable: "permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_role_permission_overrides_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "platform_access",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_role_permission_overrides_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_role_permission_overrides_CompanyId_PermissionId",
                schema: "platform_access",
                table: "company_role_permission_overrides",
                columns: new[] { "CompanyId", "PermissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_company_role_permission_overrides_PermissionId",
                schema: "platform_access",
                table: "company_role_permission_overrides",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_company_role_permission_overrides_RoleId",
                schema: "platform_access",
                table: "company_role_permission_overrides",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_company_role_permission_overrides_UpdatedByUserId",
                schema: "platform_access",
                table: "company_role_permission_overrides",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Company role permission settings cannot be dropped automatically.");
        }
    }
}
