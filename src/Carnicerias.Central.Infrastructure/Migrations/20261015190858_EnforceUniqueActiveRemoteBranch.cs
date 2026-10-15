using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Central.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueActiveRemoteBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_installations_RemoteCompanyId_RemoteBranchId",
                schema: "central_access",
                table: "installations",
                columns: ["RemoteCompanyId", "RemoteBranchId"],
                unique: true,
                filter: "\"IsActive\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_installations_RemoteCompanyId_RemoteBranchId",
                schema: "central_access",
                table: "installations");
        }
    }
}
