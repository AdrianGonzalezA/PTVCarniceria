using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScaleLabelReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScaleLabelReference",
                schema: "pos_sales",
                table: "sale_draft_lines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScaleLabelReference",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScaleLabelReference",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropColumn(
                name: "ScaleLabelReference",
                schema: "pos_sales",
                table: "confirmed_sale_lines");
        }
    }
}
