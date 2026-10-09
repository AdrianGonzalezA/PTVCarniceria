using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrectCustomerCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                schema: "customers_credit",
                table: "collection_receipts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                schema: "customers_credit",
                table: "collection_receipts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesReceiptId",
                schema: "customers_credit",
                table: "collection_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAtUtc",
                schema: "customers_credit",
                table: "collection_receipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "collection_corrections",
                schema: "customers_credit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacementReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    PosTerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_corrections", x => x.Id);
                    table.CheckConstraint("CK_collection_corrections_amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_collection_corrections_kind", "\"Kind\" IN (0, 1) AND ((\"Kind\" = 0 AND \"ReplacementReceiptId\" IS NOT NULL) OR (\"Kind\" = 1 AND \"ReplacementReceiptId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_collection_corrections_cashier_shifts_CompanyId_CashierShif~",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_corrections_collection_receipts_CompanyId_Origin~",
                        columns: x => new { x.CompanyId, x.OriginalReceiptId },
                        principalSchema: "customers_credit",
                        principalTable: "collection_receipts",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_corrections_collection_receipts_CompanyId_Replac~",
                        columns: x => new { x.CompanyId, x.ReplacementReceiptId },
                        principalSchema: "customers_credit",
                        principalTable: "collection_receipts",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_corrections_pos_terminals_CompanyId_BranchId_Pos~",
                        columns: x => new { x.CompanyId, x.BranchId, x.PosTerminalId },
                        principalSchema: "platform_access",
                        principalTable: "pos_terminals",
                        principalColumns: ["CompanyId", "BranchId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_corrections_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collection_receipts_CompanyId_ReplacesReceiptId",
                schema: "customers_credit",
                table: "collection_receipts",
                columns: ["CompanyId", "ReplacesReceiptId"]);

            migrationBuilder.AddCheckConstraint(
                name: "CK_collection_receipts_origin",
                schema: "customers_credit",
                table: "collection_receipts",
                sql: "\"Origin\" IN (0, 1) AND ((\"Origin\" = 0 AND \"ReplacesReceiptId\" IS NULL) OR (\"Origin\" = 1 AND \"ReplacesReceiptId\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_collection_receipts_void",
                schema: "customers_credit",
                table: "collection_receipts",
                sql: "\"IsVoided\" = (\"VoidedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_collection_corrections_CashierId",
                schema: "customers_credit",
                table: "collection_corrections",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_collection_corrections_CompanyId_BranchId_PosTerminalId",
                schema: "customers_credit",
                table: "collection_corrections",
                columns: ["CompanyId", "BranchId", "PosTerminalId"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_corrections_CompanyId_CashierShiftId",
                schema: "customers_credit",
                table: "collection_corrections",
                columns: ["CompanyId", "CashierShiftId"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_corrections_CompanyId_CreatedAtUtc",
                schema: "customers_credit",
                table: "collection_corrections",
                columns: ["CompanyId", "CreatedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_corrections_CompanyId_OperationId",
                schema: "customers_credit",
                table: "collection_corrections",
                columns: ["CompanyId", "OperationId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_corrections_CompanyId_OriginalReceiptId",
                schema: "customers_credit",
                table: "collection_corrections",
                columns: ["CompanyId", "OriginalReceiptId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_corrections_CompanyId_ReplacementReceiptId",
                schema: "customers_credit",
                table: "collection_corrections",
                columns: ["CompanyId", "ReplacementReceiptId"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_corrections_CorrectionNumber",
                schema: "customers_credit",
                table: "collection_corrections",
                column: "CorrectionNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_collection_receipts_collection_receipts_CompanyId_ReplacesR~",
                schema: "customers_credit",
                table: "collection_receipts",
                columns: ["CompanyId", "ReplacesReceiptId"],
                principalSchema: "customers_credit",
                principalTable: "collection_receipts",
                principalColumns: ["CompanyId", "Id"],
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES ('55325552-1227-40b0-847d-641708ac1fa6', 'pos.account.correct',
                    'Corregir cobranzas de cuenta corriente con constancia interna')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" = 'administrator' AND permission."Code" = 'pos.account.correct'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM platform_access.role_permissions
                WHERE "PermissionId" = '55325552-1227-40b0-847d-641708ac1fa6';
                DELETE FROM platform_access.permissions
                WHERE "Id" = '55325552-1227-40b0-847d-641708ac1fa6';
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_collection_receipts_collection_receipts_CompanyId_ReplacesR~",
                schema: "customers_credit",
                table: "collection_receipts");

            migrationBuilder.DropTable(
                name: "collection_corrections",
                schema: "customers_credit");

            migrationBuilder.DropIndex(
                name: "IX_collection_receipts_CompanyId_ReplacesReceiptId",
                schema: "customers_credit",
                table: "collection_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_collection_receipts_origin",
                schema: "customers_credit",
                table: "collection_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_collection_receipts_void",
                schema: "customers_credit",
                table: "collection_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                schema: "customers_credit",
                table: "collection_receipts");

            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "customers_credit",
                table: "collection_receipts");

            migrationBuilder.DropColumn(
                name: "ReplacesReceiptId",
                schema: "customers_credit",
                table: "collection_receipts");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                schema: "customers_credit",
                table: "collection_receipts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger",
                sql: "\"Kind\" IN (0, 1, 2, 3)");
        }
    }
}
