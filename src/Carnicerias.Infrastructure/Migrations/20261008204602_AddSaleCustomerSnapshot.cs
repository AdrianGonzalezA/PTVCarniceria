using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleCustomerSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.AddColumn<string>(
                name: "CustomerCode",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Preserve the identity of sales already charged to an account before enforcing the snapshot.
            migrationBuilder.Sql("""
                UPDATE pos_sales.confirmed_sales AS sale
                SET "CustomerCode" = customer."Code", "CustomerName" = customer."Name"
                FROM customers_credit.customers AS customer
                WHERE sale."CustomerId" = customer."Id"
                  AND sale."CompanyId" = customer."CompanyId"
                  AND sale."AccountChargeAmount" > 0
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales",
                sql: "\"AccountChargeAmount\" >= 0 AND \"AccountChargeAmount\" <= \"Total\" AND (\"AccountChargeAmount\" = 0 OR (\"CustomerId\" IS NOT NULL AND \"CustomerCode\" IS NOT NULL AND \"CustomerName\" IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "CustomerCode",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales",
                sql: "\"AccountChargeAmount\" >= 0 AND \"AccountChargeAmount\" <= \"Total\" AND (\"AccountChargeAmount\" = 0 OR \"CustomerId\" IS NOT NULL)");
        }
    }
}
