using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPointPaymentIntents : Migration
    {
        private static readonly string[] CompanyShiftColumns = ["CompanyId", "CashierShiftId"];
        private static readonly string[] CompanyTerminalColumns = ["CompanyId", "BranchId", "PosTerminalId"];
        private static readonly string[] CompanyShiftPrincipals = ["CompanyId", "Id"];
        private static readonly string[] CompanyTerminalPrincipals = ["CompanyId", "BranchId", "Id"];
        private static readonly string[] CompanyDraftCreatedColumns = ["CompanyId", "SaleDraftId", "CreatedAtUtc"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "point_payment_intents",
                schema: "payments_cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PosTerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderOrderStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProviderPaymentStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProviderPaymentStatusDetail = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_payment_intents", x => x.Id);
                    table.CheckConstraint("CK_point_payment_intents_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_point_payment_intents_status", "\"Status\" BETWEEN 0 AND 7");
                    table.ForeignKey(
                        name: "FK_point_payment_intents_cashier_shifts_CompanyId_CashierShift~",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: CompanyShiftPrincipals,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_point_payment_intents_pos_terminals_CompanyId_BranchId_PosT~",
                        columns: x => new { x.CompanyId, x.BranchId, x.PosTerminalId },
                        principalSchema: "platform_access",
                        principalTable: "pos_terminals",
                        principalColumns: CompanyTerminalPrincipals,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_point_payment_intents_sale_drafts_CompanyId_SaleDraftId",
                        columns: x => new { x.CompanyId, x.SaleDraftId },
                        principalSchema: "pos_sales",
                        principalTable: "sale_drafts",
                        principalColumns: CompanyShiftPrincipals,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_point_payment_intents_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CashierId",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: CompanyTerminalColumns);

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_CashierShiftId",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: CompanyShiftColumns);

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId_CreatedAtUtc",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: CompanyDraftCreatedColumns);

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_IdempotencyKey",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_ProviderOrderId",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "ProviderOrderId",
                unique: true,
                filter: "\"ProviderOrderId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "point_payment_intents",
                schema: "payments_cash");
        }
    }
}
