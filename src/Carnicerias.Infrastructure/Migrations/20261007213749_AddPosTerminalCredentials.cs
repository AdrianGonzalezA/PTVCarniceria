using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosTerminalCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CredentialHash",
                schema: "platform_access",
                table: "pos_terminals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_terminals_CredentialHash",
                schema: "platform_access",
                table: "pos_terminals",
                column: "CredentialHash",
                unique: true,
                filter: "\"CredentialHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pos_terminals_CredentialHash",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropColumn(
                name: "CredentialHash",
                schema: "platform_access",
                table: "pos_terminals");
        }
    }
}
