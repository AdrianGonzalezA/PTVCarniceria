using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleCheckoutProcess : Migration
    {
        private static readonly string[] CompanyIdKey = ["CompanyId", "Id"];
        private static readonly string[] CompanyBranchIdKey = ["CompanyId", "BranchId", "Id"];
        private static readonly string[] CompanyDraftKey = ["CompanyId", "SaleDraftId"];
        private static readonly string[] CheckoutCreatedKey = ["SaleCheckoutId", "CreatedAtUtc"];
        private static readonly string[] CheckoutStatusKey = ["SaleCheckoutId", "Status"];
        private static readonly string[] CompanyTerminalKey = ["CompanyId", "BranchId", "PosTerminalId"];
        private static readonly string[] CompanyShiftStatusKey = ["CompanyId", "CashierShiftId", "Status"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutInstrumentId",
                schema: "payments_cash",
                table: "sale_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutInstrumentId",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sale_payment_processes",
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
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DraftUpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    TermsJson = table.Column<string>(type: "jsonb", nullable: false),
                    PlanHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommitKey = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedSaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_payment_processes", x => x.Id);
                    table.UniqueConstraint("AK_sale_payment_processes_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_sale_payment_processes_revision", "\"Revision\" >= 0");
                    table.CheckConstraint("CK_sale_payment_processes_status", "\"Status\" BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_sale_payment_processes_total", "\"Total\" > 0");
                    table.ForeignKey(
                        name: "FK_sale_payment_processes_cashier_shifts_CompanyId_CashierShif~",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: CompanyIdKey,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_payment_processes_confirmed_sales_ConfirmedSaleId",
                        column: x => x.ConfirmedSaleId,
                        principalSchema: "pos_sales",
                        principalTable: "confirmed_sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_payment_processes_pos_terminals_CompanyId_BranchId_Pos~",
                        columns: x => new { x.CompanyId, x.BranchId, x.PosTerminalId },
                        principalSchema: "platform_access",
                        principalTable: "pos_terminals",
                        principalColumns: CompanyBranchIdKey,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_payment_processes_sale_drafts_CompanyId_SaleDraftId",
                        columns: x => new { x.CompanyId, x.SaleDraftId },
                        principalSchema: "pos_sales",
                        principalTable: "sale_drafts",
                        principalColumns: CompanyIdKey,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_payment_processes_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_payment_events",
                schema: "payments_cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleCheckoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_payment_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sale_payment_events_sale_payment_processes_SaleCheckoutId",
                        column: x => x.SaleCheckoutId,
                        principalSchema: "payments_cash",
                        principalTable: "sale_payment_processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_payment_events_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_payment_instruments",
                schema: "payments_cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleCheckoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PointPaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_payment_instruments", x => x.Id);
                    table.CheckConstraint("CK_sale_payment_instruments_amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_sale_payment_instruments_status", "\"Status\" BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_sale_payment_instruments_point_payment_intents_PointPayment~",
                        column: x => x.PointPaymentIntentId,
                        principalSchema: "payments_cash",
                        principalTable: "point_payment_intents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_payment_instruments_sale_payment_processes_SaleCheckou~",
                        column: x => x.SaleCheckoutId,
                        principalSchema: "payments_cash",
                        principalTable: "sale_payment_processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_CheckoutInstrumentId",
                schema: "payments_cash",
                table: "sale_payments",
                column: "CheckoutInstrumentId",
                unique: true,
                filter: "\"CheckoutInstrumentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CheckoutInstrumentId",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "CheckoutInstrumentId",
                unique: true,
                filter: "\"CheckoutInstrumentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: CompanyDraftKey,
                unique: true,
                filter: "\"SaleDraftId\" IS NOT NULL AND \"CheckoutInstrumentId\" IS NULL AND \"Status\" IN (0, 1, 2, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "ConfirmedSaleId",
                filter: "\"ConfirmedSaleId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_events_ActorUserId",
                schema: "payments_cash",
                table: "sale_payment_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_events_SaleCheckoutId_CreatedAtUtc",
                schema: "payments_cash",
                table: "sale_payment_events",
                columns: CheckoutCreatedKey);

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_instruments_PointPaymentIntentId",
                schema: "payments_cash",
                table: "sale_payment_instruments",
                column: "PointPaymentIntentId",
                unique: true,
                filter: "\"PointPaymentIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_instruments_SaleCheckoutId_Status",
                schema: "payments_cash",
                table: "sale_payment_instruments",
                columns: CheckoutStatusKey);

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_processes_CashierId",
                schema: "payments_cash",
                table: "sale_payment_processes",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_processes_CommitKey",
                schema: "payments_cash",
                table: "sale_payment_processes",
                column: "CommitKey",
                unique: true,
                filter: "\"CommitKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_processes_CompanyId_BranchId_PosTerminalId",
                schema: "payments_cash",
                table: "sale_payment_processes",
                columns: CompanyTerminalKey);

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_processes_CompanyId_CashierShiftId_Status",
                schema: "payments_cash",
                table: "sale_payment_processes",
                columns: CompanyShiftStatusKey);

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_processes_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "sale_payment_processes",
                columns: CompanyDraftKey,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_payment_processes_ConfirmedSaleId",
                schema: "payments_cash",
                table: "sale_payment_processes",
                column: "ConfirmedSaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_point_payment_intents_sale_payment_instruments_CheckoutInst~",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "CheckoutInstrumentId",
                principalSchema: "payments_cash",
                principalTable: "sale_payment_instruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_payments_sale_payment_instruments_CheckoutInstrumentId",
                schema: "payments_cash",
                table: "sale_payments",
                column: "CheckoutInstrumentId",
                principalSchema: "payments_cash",
                principalTable: "sale_payment_instruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM payments_cash.sale_payment_processes) THEN
                        RAISE EXCEPTION 'I03a checkout data must be reconciled before schema rollback';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_point_payment_intents_sale_payment_instruments_CheckoutInst~",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_payments_sale_payment_instruments_CheckoutInstrumentId",
                schema: "payments_cash",
                table: "sale_payments");

            migrationBuilder.DropTable(
                name: "sale_payment_events",
                schema: "payments_cash");

            migrationBuilder.DropTable(
                name: "sale_payment_instruments",
                schema: "payments_cash");

            migrationBuilder.DropTable(
                name: "sale_payment_processes",
                schema: "payments_cash");

            migrationBuilder.DropIndex(
                name: "IX_sale_payments_CheckoutInstrumentId",
                schema: "payments_cash",
                table: "sale_payments");

            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_CheckoutInstrumentId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropColumn(
                name: "CheckoutInstrumentId",
                schema: "payments_cash",
                table: "sale_payments");

            migrationBuilder.DropColumn(
                name: "CheckoutInstrumentId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: CompanyDraftKey,
                unique: true,
                filter: "\"SaleDraftId\" IS NOT NULL AND \"Status\" IN (0, 1, 2, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "ConfirmedSaleId",
                unique: true,
                filter: "\"ConfirmedSaleId\" IS NOT NULL");
        }
    }
}
