using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedBranchCentralConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_central_connections",
                schema: "platform_access",
                columns: table => new
                {
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CredentialSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreviousCredentialSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreviousCredentialExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_central_connections", x => x.BranchId);
                    table.ForeignKey(
                        name: "FK_branch_central_connections_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_branch_central_connections_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_branch_central_connections_CompanyId_BranchId",
                schema: "platform_access",
                table: "branch_central_connections",
                columns: ["CompanyId", "BranchId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branch_central_connections_InstallationId",
                schema: "platform_access",
                table: "branch_central_connections",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branch_central_connections_UpdatedByUserId",
                schema: "platform_access",
                table: "branch_central_connections",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_central_connections",
                schema: "platform_access");
        }
    }
}
