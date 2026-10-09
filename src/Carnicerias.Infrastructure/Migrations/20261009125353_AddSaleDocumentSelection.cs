using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleDocumentSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentType",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RecipientAddress",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientDocumentNumber",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecipientTaxStatus",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_sales_document_type",
                schema: "pos_sales",
                table: "confirmed_sales",
                sql: "\"DocumentType\" IN (0, 1, 2) AND \"RecipientTaxStatus\" IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_sales_document_type",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "RecipientAddress",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "RecipientDocumentNumber",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "RecipientName",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "RecipientTaxStatus",
                schema: "pos_sales",
                table: "confirmed_sales");
        }
    }
}
