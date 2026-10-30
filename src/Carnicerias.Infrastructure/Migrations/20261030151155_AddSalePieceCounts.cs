using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalePieceCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "pos_sales",
                table: "sale_draft_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("UPDATE pos_sales.sale_draft_lines SET \"PieceCount\" = 1 WHERE \"InventoryPieceId\" IS NOT NULL");
            migrationBuilder.Sql("UPDATE pos_sales.confirmed_sale_lines SET \"PieceCount\" = 1 WHERE \"InventoryPieceId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_sale_draft_lines_piece_count",
                schema: "pos_sales",
                table: "sale_draft_lines",
                sql: "\"PieceCount\" IS NULL OR \"PieceCount\" BETWEEN 1 AND 10000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_sale_lines_piece_count",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                sql: "\"PieceCount\" IS NULL OR \"PieceCount\" BETWEEN 1 AND 10000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_sale_draft_lines_piece_count",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_sale_lines_piece_count",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "pos_sales",
                table: "confirmed_sale_lines");
        }
    }
}
