using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleTaxSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NetAfterDiscount",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderDiscountAmount",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxRuleId",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxTreatment",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxableBase",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NetAfterDiscount",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "OrderDiscountAmount",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "TaxRuleId",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "TaxTreatment",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "TaxableBase",
                schema: "pos_sales",
                table: "confirmed_sale_lines");
        }
    }
}
