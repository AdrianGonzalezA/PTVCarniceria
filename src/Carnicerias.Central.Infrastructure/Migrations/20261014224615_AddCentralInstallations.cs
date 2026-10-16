using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Central.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralInstallations : Migration
    {
        private static readonly string[] CompanyActiveLabelIndexColumns =
            ["CentralCompanyId", "IsActive", "Label"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installations",
                schema: "central_access",
                columns: table => new
                {
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CentralCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DestinationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SecretReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installations", x => x.InstallationId);
                    table.ForeignKey(
                        name: "FK_installations_companies_CentralCompanyId",
                        column: x => x.CentralCompanyId,
                        principalSchema: "central_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_installations_CentralCompanyId_IsActive_Label",
                schema: "central_access",
                table: "installations",
                columns: CompanyActiveLabelIndexColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installations",
                schema: "central_access");
        }
    }
}
