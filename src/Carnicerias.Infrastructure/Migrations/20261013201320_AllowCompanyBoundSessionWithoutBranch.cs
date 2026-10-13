using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowCompanyBoundSessionWithoutBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_sessions_context_pair",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_sessions_context_pair",
                schema: "platform_access",
                table: "sessions",
                sql: "\"BranchId\" IS NULL OR \"CompanyId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_sessions_context_pair",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_sessions_context_pair",
                schema: "platform_access",
                table: "sessions",
                sql: "(\"CompanyId\" IS NULL AND \"BranchId\" IS NULL) OR (\"CompanyId\" IS NOT NULL AND \"BranchId\" IS NOT NULL)");
        }
    }
}
