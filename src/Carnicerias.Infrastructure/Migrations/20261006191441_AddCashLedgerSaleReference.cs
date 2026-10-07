using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashLedgerSaleReference : Migration
    {
        private static readonly string[] CompanySaleColumns = ["CompanyId", "SaleId"];
        private static readonly string[] CompanySalePrincipalColumns = ["CompanyId", "Id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                schema: "payments_cash",
                table: "cash_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CompanyId_SaleId",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CompanySaleColumns);

            migrationBuilder.AddForeignKey(
                name: "FK_cash_ledger_confirmed_sales_CompanyId_SaleId",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CompanySaleColumns,
                principalSchema: "pos_sales",
                principalTable: "confirmed_sales",
                principalColumns: CompanySalePrincipalColumns,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cash_ledger_confirmed_sales_CompanyId_SaleId",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.DropIndex(
                name: "IX_cash_ledger_CompanyId_SaleId",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.DropColumn(
                name: "SaleId",
                schema: "payments_cash",
                table: "cash_ledger");
        }
    }
}
