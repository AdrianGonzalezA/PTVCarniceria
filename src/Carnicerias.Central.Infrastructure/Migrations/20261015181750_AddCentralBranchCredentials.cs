using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Central.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralBranchCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_credentials",
                schema: "central_access",
                columns: table => new
                {
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", maxLength: 12, nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    Tag = table.Column<byte[]>(type: "bytea", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_credentials", x => x.InstallationId);
                    table.ForeignKey(
                        name: "FK_branch_credentials_installations_InstallationId",
                        column: x => x.InstallationId,
                        principalSchema: "central_access",
                        principalTable: "installations",
                        principalColumn: "InstallationId",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_credentials",
                schema: "central_access");
        }
    }
}
