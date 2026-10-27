using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCollectionChangeMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4)");
        }
    }
}
