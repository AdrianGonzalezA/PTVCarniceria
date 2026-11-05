using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF generates constant arrays for keys and indexes.

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftClosingReportsI17 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift_closing_reports",
                schema: "payments_cash",
                columns: table => new
                {
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_closing_reports", x => x.CashierShiftId);
                    table.ForeignKey(
                        name: "FK_shift_closing_reports_cashier_shifts_CompanyId_CashierShift~",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shift_closing_reports_CompanyId_CashierShiftId",
                schema: "payments_cash",
                table: "shift_closing_reports",
                columns: new[] { "CompanyId", "CashierShiftId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shift_closing_reports_CompanyId_CreatedAtUtc",
                schema: "payments_cash",
                table: "shift_closing_reports",
                columns: new[] { "CompanyId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "I17 closing reports are immutable commercial records. Roll back application code without dropping them.");
        }
    }
}
