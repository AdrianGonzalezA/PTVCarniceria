using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatformAccess : Migration
    {
        private static readonly string[] BranchCompanyKeyColumns = ["CompanyId", "Id"];
        private static readonly string[] AssignmentCompanyBranchColumns = ["CompanyId", "BranchId"];
        private static readonly string[] CompanyAssignmentColumns = ["UserId", "RoleId", "CompanyId"];
        private static readonly string[] BranchAssignmentColumns = ["UserId", "RoleId", "CompanyId", "BranchId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform_access");

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "platform_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "platform_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "platform_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "platform_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UsernameNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    EmailNormalized = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "platform_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branches", x => x.Id);
                    table.UniqueConstraint("AK_branches_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_branches_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "platform_access",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalSchema: "platform_access",
                        principalTable: "permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "platform_access",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_assignments",
                schema: "platform_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_assignments_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: BranchCompanyKeyColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_assignments_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_assignments_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "platform_access",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_assignments_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_permissions_Code",
                schema: "platform_access",
                table: "permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_PermissionId",
                schema: "platform_access",
                table: "role_permissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_roles_Code",
                schema: "platform_access",
                table: "roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_assignments_CompanyId_BranchId",
                schema: "platform_access",
                table: "user_assignments",
                columns: AssignmentCompanyBranchColumns);

            migrationBuilder.CreateIndex(
                name: "IX_user_assignments_RoleId",
                schema: "platform_access",
                table: "user_assignments",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_user_assignments_UserId_RoleId_CompanyId",
                schema: "platform_access",
                table: "user_assignments",
                columns: CompanyAssignmentColumns,
                unique: true,
                filter: "\"BranchId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_assignments_UserId_RoleId_CompanyId_BranchId",
                schema: "platform_access",
                table: "user_assignments",
                columns: BranchAssignmentColumns,
                unique: true,
                filter: "\"BranchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_EmailNormalized",
                schema: "platform_access",
                table: "users",
                column: "EmailNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_UsernameNormalized",
                schema: "platform_access",
                table: "users",
                column: "UsernameNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "platform_access");

            migrationBuilder.DropTable(
                name: "user_assignments",
                schema: "platform_access");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "platform_access");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "platform_access");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "platform_access");

            migrationBuilder.DropTable(
                name: "users",
                schema: "platform_access");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "platform_access");

            migrationBuilder.DropSchema(
                name: "platform_access");
        }
    }
}
