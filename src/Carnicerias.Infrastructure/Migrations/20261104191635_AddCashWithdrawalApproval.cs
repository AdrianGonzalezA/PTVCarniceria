using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core scaffolding emits constant column arrays in migrations.

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashWithdrawalApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.CreateTable(
                name: "cash_withdrawal_requests",
                schema: "pos_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PosTerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LedgerMovementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_withdrawal_requests", x => x.Id);
                    table.CheckConstraint("CK_cash_withdrawal_cash", "\"Method\" = 0 AND \"Amount\" > 0");
                    table.CheckConstraint("CK_cash_withdrawal_decision", "(\"Status\" = 0 AND \"DecidedByUserId\" IS NULL AND \"DecidedAtUtc\" IS NULL AND \"DecisionReason\" IS NULL AND \"LedgerMovementId\" IS NULL) OR (\"Status\" = 1 AND \"DecidedByUserId\" IS NOT NULL AND \"DecidedAtUtc\" IS NOT NULL AND \"DecisionReason\" IS NULL AND \"LedgerMovementId\" IS NOT NULL) OR (\"Status\" = 2 AND \"DecidedByUserId\" IS NOT NULL AND \"DecidedAtUtc\" IS NOT NULL AND \"DecisionReason\" IS NOT NULL AND \"LedgerMovementId\" IS NULL)");
                    table.CheckConstraint("CK_cash_withdrawal_status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_cash_withdrawal_requests_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_withdrawal_requests_cash_ledger_LedgerMovementId",
                        column: x => x.LedgerMovementId,
                        principalSchema: "payments_cash",
                        principalTable: "cash_ledger",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_withdrawal_requests_cashier_shifts_CompanyId_CashierSh~",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_withdrawal_requests_pos_terminals_CompanyId_BranchId_P~",
                        columns: x => new { x.CompanyId, x.BranchId, x.PosTerminalId },
                        principalSchema: "platform_access",
                        principalTable: "pos_terminals",
                        principalColumns: new[] { "CompanyId", "BranchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_withdrawal_requests_users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_withdrawal_requests_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawal_requests_CompanyId_BranchId_OperationId",
                schema: "pos_sales",
                table: "cash_withdrawal_requests",
                columns: new[] { "CompanyId", "BranchId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawal_requests_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "cash_withdrawal_requests",
                columns: new[] { "CompanyId", "BranchId", "PosTerminalId" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawal_requests_CompanyId_BranchId_Status_Requeste~",
                schema: "pos_sales",
                table: "cash_withdrawal_requests",
                columns: new[] { "CompanyId", "BranchId", "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawal_requests_CompanyId_CashierShiftId_Status",
                schema: "pos_sales",
                table: "cash_withdrawal_requests",
                columns: new[] { "CompanyId", "CashierShiftId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawal_requests_DecidedByUserId",
                schema: "pos_sales",
                table: "cash_withdrawal_requests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawal_requests_LedgerMovementId",
                schema: "pos_sales",
                table: "cash_withdrawal_requests",
                column: "LedgerMovementId",
                unique: true,
                filter: "\"LedgerMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawal_requests_RequestedByUserId",
                schema: "pos_sales",
                table: "cash_withdrawal_requests",
                column: "RequestedByUserId");

            migrationBuilder.Sql("""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES ('cd0139e1-a0ad-4377-8e9f-c593ab0f2481',
                        'cash.withdrawal.approve',
                        'Aprobar o rechazar retiros extraordinarios de efectivo')
                ON CONFLICT ("Code") DO NOTHING;
                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" = 'administrator'
                  AND permission."Code" = 'cash.withdrawal.approve'
                ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pos_sales.cash_withdrawal_requests) THEN
                        RAISE EXCEPTION 'I16 withdrawal requests exist; a data-preserving rollback is required';
                    END IF;
                END $guard$;
                """);
            migrationBuilder.Sql("""
                DELETE FROM platform_access.role_permissions
                WHERE "PermissionId" = 'cd0139e1-a0ad-4377-8e9f-c593ab0f2481';
                DELETE FROM platform_access.permissions
                WHERE "Id" = 'cd0139e1-a0ad-4377-8e9f-c593ab0f2481'
                  AND "Code" = 'cash.withdrawal.approve';
                """);
            migrationBuilder.DropTable(
                name: "cash_withdrawal_requests",
                schema: "pos_sales");

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
    }
}
