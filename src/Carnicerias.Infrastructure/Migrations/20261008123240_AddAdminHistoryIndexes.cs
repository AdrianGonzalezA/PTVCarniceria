using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminHistoryIndexes : Migration
    {
        private static readonly string[] CompanyBranchDate = ["CompanyId", "BranchId", "CreatedAtUtc"];
        private static readonly string[] CompanyDate = ["CompanyId", "CreatedAtUtc"];
        private static readonly string[] CompanyBranchTerminal = ["CompanyId", "BranchId", "PosTerminalId"];
        private static readonly string[] CompanyBranchTerminalSaleDate = ["CompanyId", "BranchId", "PosTerminalId", "ConfirmedAtUtc"];
        private static readonly string[] CompanySaleDate = ["CompanyId", "ConfirmedAtUtc"];
        private static readonly string[] CompanyBranchTerminalShiftDate = ["CompanyId", "BranchId", "PosTerminalId", "OpenedAtUtc"];
        private static readonly string[] CompanyShiftDate = ["CompanyId", "OpenedAtUtc"];
        private static readonly string[] CompanyBranchTerminalCashDate = ["CompanyId", "BranchId", "PosTerminalId", "CreatedAtUtc"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropIndex(
                name: "IX_cash_ledger_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_CompanyId_BranchId_CreatedAtUtc",
                schema: "inventory",
                table: "inventory_movements",
                columns: CompanyBranchDate);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_CompanyId_CreatedAtUtc",
                schema: "inventory",
                table: "inventory_movements",
                columns: CompanyDate);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId_PosTerminalId_ConfirmedA~",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyBranchTerminalSaleDate);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_ConfirmedAtUtc",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanySaleDate);

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_CompanyId_BranchId_PosTerminalId_OpenedAtUtc",
                schema: "payments_cash",
                table: "cashier_shifts",
                columns: CompanyBranchTerminalShiftDate);

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_CompanyId_OpenedAtUtc",
                schema: "payments_cash",
                table: "cashier_shifts",
                columns: CompanyShiftDate);

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CompanyId_BranchId_PosTerminalId_CreatedAtUtc",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CompanyBranchTerminalCashDate);

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CompanyId_CreatedAtUtc",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CompanyDate);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inventory_movements_CompanyId_BranchId_CreatedAtUtc",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "IX_inventory_movements_CompanyId_CreatedAtUtc",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId_PosTerminalId_ConfirmedA~",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropIndex(
                name: "IX_confirmed_sales_CompanyId_ConfirmedAtUtc",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropIndex(
                name: "IX_cashier_shifts_CompanyId_BranchId_PosTerminalId_OpenedAtUtc",
                schema: "payments_cash",
                table: "cashier_shifts");

            migrationBuilder.DropIndex(
                name: "IX_cashier_shifts_CompanyId_OpenedAtUtc",
                schema: "payments_cash",
                table: "cashier_shifts");

            migrationBuilder.DropIndex(
                name: "IX_cash_ledger_CompanyId_BranchId_PosTerminalId_CreatedAtUtc",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.DropIndex(
                name: "IX_cash_ledger_CompanyId_CreatedAtUtc",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyBranchTerminal);

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CompanyBranchTerminal);
        }
    }
}
