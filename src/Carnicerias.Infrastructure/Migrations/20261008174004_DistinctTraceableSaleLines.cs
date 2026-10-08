using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DistinctTraceableSaleLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryPieceId",
                schema: "pos_sales",
                table: "sale_draft_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PieceIdentifier",
                schema: "pos_sales",
                table: "sale_draft_lines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryPieceId",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PieceIdentifier",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_lines_InventoryPieceId",
                schema: "pos_sales",
                table: "sale_draft_lines",
                column: "InventoryPieceId");

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sale_lines_InventoryPieceId",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                column: "InventoryPieceId",
                unique: true,
                filter: "\"InventoryPieceId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_sale_lines_pieces_InventoryPieceId",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                column: "InventoryPieceId",
                principalSchema: "inventory",
                principalTable: "pieces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_draft_lines_pieces_InventoryPieceId",
                schema: "pos_sales",
                table: "sale_draft_lines",
                column: "InventoryPieceId",
                principalSchema: "inventory",
                principalTable: "pieces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_confirmed_sale_lines_pieces_InventoryPieceId",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_draft_lines_pieces_InventoryPieceId",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropIndex(
                name: "IX_sale_draft_lines_InventoryPieceId",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropIndex(
                name: "IX_confirmed_sale_lines_InventoryPieceId",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "InventoryPieceId",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropColumn(
                name: "PieceIdentifier",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropColumn(
                name: "InventoryPieceId",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "PieceIdentifier",
                schema: "pos_sales",
                table: "confirmed_sale_lines");
        }
    }
}
