using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashierShifts : Migration
    {
        private static readonly string[] BranchPrincipalColumns = ["CompanyId", "Id"];
        private static readonly string[] OpenShiftOwnerColumns = ["CompanyId", "BranchId", "CashierId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments_cash");

            migrationBuilder.CreateTable(
                name: "cashier_shifts",
                schema: "payments_cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cashier_shifts", x => x.Id);
                    table.UniqueConstraint("AK_cashier_shifts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_cashier_shifts_opening_cash_nonnegative", "\"OpeningCash\" >= 0");
                    table.CheckConstraint("CK_cashier_shifts_status", "\"Status\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_cashier_shifts_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: BranchPrincipalColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cashier_shifts_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_CashierId",
                schema: "payments_cash",
                table: "cashier_shifts",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_CompanyId_BranchId_CashierId",
                schema: "payments_cash",
                table: "cashier_shifts",
                columns: OpenShiftOwnerColumns,
                unique: true,
                filter: "\"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cashier_shifts",
                schema: "payments_cash");
        }
    }
}
