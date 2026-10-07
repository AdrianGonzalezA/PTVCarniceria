using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosTerminals : Migration
    {
        private static readonly string[] CompanyIdColumns = ["CompanyId", "Id"];
        private static readonly string[] CompanyBranchColumns = ["CompanyId", "BranchId"];
        private static readonly string[] CompanyBranchIdColumns = ["CompanyId", "BranchId", "Id"];
        private static readonly string[] CompanyBranchNameColumns = ["CompanyId", "BranchId", "Name"];
        private static readonly string[] CompanyBranchTerminalColumns = ["CompanyId", "BranchId", "PosTerminalId"];
        private static readonly string[] CompanyShiftColumns = ["CompanyId", "CashierShiftId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.AddColumn<Guid>(
                name: "PosTerminalId",
                schema: "platform_access",
                table: "sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CashierShiftId",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosTerminalId",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosTerminalId",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosTerminalId",
                schema: "payments_cash",
                table: "cashier_shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosTerminalId",
                schema: "payments_cash",
                table: "cash_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pos_terminals",
                schema: "platform_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsHistorical = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_terminals", x => x.Id);
                    table.UniqueConstraint("AK_pos_terminals_CompanyId_BranchId_Id", x => new { x.CompanyId, x.BranchId, x.Id });
                    table.ForeignKey(
                        name: "FK_pos_terminals_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pos_sales.sale_drafts AS draft
                        WHERE draft."Status" = 0
                          AND NOT EXISTS (
                              SELECT 1
                              FROM payments_cash.cashier_shifts AS shift
                              WHERE shift."CompanyId" = draft."CompanyId"
                                AND shift."BranchId" = draft."BranchId"
                                AND shift."CashierId" = draft."UserId"
                                AND shift."Status" = 0)) THEN
                        RAISE EXCEPTION 'Active legacy sale draft has no open cashier shift; resolve it before migrating';
                    END IF;
                END $$;

                INSERT INTO platform_access.pos_terminals
                    ("Id", "CompanyId", "BranchId", "Name", "IsActive", "IsHistorical")
                SELECT gen_random_uuid(), branch."CompanyId", branch."Id", 'Caja histórica', FALSE, TRUE
                FROM platform_access.branches AS branch;

                UPDATE payments_cash.cashier_shifts AS shift
                SET "PosTerminalId" = terminal."Id"
                FROM platform_access.pos_terminals AS terminal
                WHERE terminal."CompanyId" = shift."CompanyId"
                  AND terminal."BranchId" = shift."BranchId"
                  AND terminal."IsHistorical" = TRUE;

                UPDATE pos_sales.confirmed_sales AS sale
                SET "PosTerminalId" = shift."PosTerminalId"
                FROM payments_cash.cashier_shifts AS shift
                WHERE sale."CashierShiftId" = shift."Id"
                  AND sale."CompanyId" = shift."CompanyId";

                UPDATE payments_cash.cash_ledger AS movement
                SET "PosTerminalId" = shift."PosTerminalId"
                FROM payments_cash.cashier_shifts AS shift
                WHERE movement."CashierShiftId" = shift."Id"
                  AND movement."CompanyId" = shift."CompanyId";

                UPDATE pos_sales.sale_drafts AS draft
                SET "PosTerminalId" = terminal."Id"
                FROM platform_access.pos_terminals AS terminal
                WHERE terminal."CompanyId" = draft."CompanyId"
                  AND terminal."BranchId" = draft."BranchId"
                  AND terminal."IsHistorical" = TRUE;

                UPDATE pos_sales.sale_drafts AS draft
                SET "CashierShiftId" = shift."Id"
                FROM payments_cash.cashier_shifts AS shift
                WHERE draft."Status" = 0
                  AND shift."CompanyId" = draft."CompanyId"
                  AND shift."BranchId" = draft."BranchId"
                  AND shift."CashierId" = draft."UserId"
                  AND shift."Status" = 0;

                UPDATE pos_sales.sale_drafts AS draft
                SET "CashierShiftId" = sale."CashierShiftId"
                FROM pos_sales.confirmed_sales AS sale
                WHERE sale."CompanyId" = draft."CompanyId"
                  AND sale."SourceDraftId" = draft."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_PosTerminalId",
                schema: "platform_access",
                table: "sessions",
                column: "PosTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: CompanyBranchTerminalColumns);

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CompanyId_CashierShiftId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: CompanyShiftColumns);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyBranchTerminalColumns);

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "cashier_shifts",
                columns: CompanyBranchTerminalColumns,
                unique: true,
                filter: "\"Status\" = 0 AND \"PosTerminalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CompanyBranchTerminalColumns);

            migrationBuilder.CreateIndex(
                name: "IX_pos_terminals_CompanyId_BranchId_Name",
                schema: "platform_access",
                table: "pos_terminals",
                columns: CompanyBranchNameColumns,
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_cash_ledger_pos_terminals_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CompanyBranchTerminalColumns,
                principalSchema: "platform_access",
                principalTable: "pos_terminals",
                principalColumns: CompanyBranchIdColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_cashier_shifts_pos_terminals_CompanyId_BranchId_PosTerminal~",
                schema: "payments_cash",
                table: "cashier_shifts",
                columns: CompanyBranchTerminalColumns,
                principalSchema: "platform_access",
                principalTable: "pos_terminals",
                principalColumns: CompanyBranchIdColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_sales_pos_terminals_CompanyId_BranchId_PosTermina~",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyBranchTerminalColumns,
                principalSchema: "platform_access",
                principalTable: "pos_terminals",
                principalColumns: CompanyBranchIdColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_drafts_cashier_shifts_CompanyId_CashierShiftId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: CompanyShiftColumns,
                principalSchema: "payments_cash",
                principalTable: "cashier_shifts",
                principalColumns: CompanyIdColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_drafts_pos_terminals_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: CompanyBranchTerminalColumns,
                principalSchema: "platform_access",
                principalTable: "pos_terminals",
                principalColumns: CompanyBranchIdColumns,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sessions_pos_terminals_PosTerminalId",
                schema: "platform_access",
                table: "sessions",
                column: "PosTerminalId",
                principalSchema: "platform_access",
                principalTable: "pos_terminals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cash_ledger_pos_terminals_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.DropForeignKey(
                name: "FK_cashier_shifts_pos_terminals_CompanyId_BranchId_PosTerminal~",
                schema: "payments_cash",
                table: "cashier_shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_confirmed_sales_pos_terminals_CompanyId_BranchId_PosTermina~",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_drafts_cashier_shifts_CompanyId_CashierShiftId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_drafts_pos_terminals_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropForeignKey(
                name: "FK_sessions_pos_terminals_PosTerminalId",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.DropTable(
                name: "pos_terminals",
                schema: "platform_access");

            migrationBuilder.DropIndex(
                name: "IX_sessions_PosTerminalId",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropIndex(
                name: "IX_sale_drafts_CompanyId_CashierShiftId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropIndex(
                name: "IX_cashier_shifts_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "cashier_shifts");

            migrationBuilder.DropIndex(
                name: "IX_cash_ledger_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.DropColumn(
                name: "PosTerminalId",
                schema: "platform_access",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "CashierShiftId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "PosTerminalId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "PosTerminalId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "PosTerminalId",
                schema: "payments_cash",
                table: "cashier_shifts");

            migrationBuilder.DropColumn(
                name: "PosTerminalId",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyBranchColumns);
        }
    }
}
