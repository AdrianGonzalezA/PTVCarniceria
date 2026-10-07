using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalContextToSessions : Migration
    {
        private static readonly string[] SessionContextColumns = ["CompanyId", "BranchId"];
        private static readonly string[] BranchContextColumns = ["CompanyId", "Id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                schema: "platform_access",
                table: "sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "platform_access",
                table: "sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_CompanyId_BranchId",
                schema: "platform_access",
                table: "sessions",
                columns: SessionContextColumns);

            migrationBuilder.AddCheckConstraint(
                name: "CK_sessions_context_pair",
                schema: "platform_access",
                table: "sessions",
                sql: "(\"CompanyId\" IS NULL AND \"BranchId\" IS NULL) OR (\"CompanyId\" IS NOT NULL AND \"BranchId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_sessions_branches_CompanyId_BranchId",
                schema: "platform_access",
                table: "sessions",
                columns: SessionContextColumns,
                principalSchema: "platform_access",
                principalTable: "branches",
                principalColumns: BranchContextColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sessions_companies_CompanyId",
                schema: "platform_access",
                table: "sessions",
                column: "CompanyId",
                principalSchema: "platform_access",
                principalTable: "companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sessions_branches_CompanyId_BranchId",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_sessions_companies_CompanyId",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "IX_sessions_CompanyId_BranchId",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_sessions_context_pair",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "platform_access",
                table: "sessions");
        }
    }
}
