using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssignPosTerminalDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrinterBaudRate",
                schema: "platform_access",
                table: "pos_terminals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrinterKind",
                schema: "platform_access",
                table: "pos_terminals",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrinterPort",
                schema: "platform_access",
                table: "pos_terminals",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScaleBaudRate",
                schema: "platform_access",
                table: "pos_terminals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScalePort",
                schema: "platform_access",
                table: "pos_terminals",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrinterBaudRate",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropColumn(
                name: "PrinterKind",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropColumn(
                name: "PrinterPort",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropColumn(
                name: "ScaleBaudRate",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropColumn(
                name: "ScalePort",
                schema: "platform_access",
                table: "pos_terminals");
        }
    }
}
