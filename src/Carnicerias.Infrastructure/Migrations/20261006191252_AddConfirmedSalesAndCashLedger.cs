using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmedSalesAndCashLedger : Migration
    {
        private static readonly string[] CompanyIdColumns = ["CompanyId", "Id"];
        private static readonly string[] CashLedgerShiftMethodCreatedColumns = ["CashierShiftId", "Method", "CreatedAtUtc"];
        private static readonly string[] CashLedgerCompanyBranchOperationColumns = ["CompanyId", "BranchId", "OperationId"];
        private static readonly string[] CashLedgerCompanyShiftColumns = ["CompanyId", "CashierShiftId"];
        private static readonly string[] CompanyProductColumns = ["CompanyId", "ProductId"];
        private static readonly string[] CompanyBranchColumns = ["CompanyId", "BranchId"];
        private static readonly string[] CompanyShiftColumns = ["CompanyId", "CashierShiftId"];
        private static readonly string[] CompanyPriceListColumns = ["CompanyId", "PriceListId"];
        private static readonly string[] CompanyDraftColumns = ["CompanyId", "SourceDraftId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAtUtc",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedSaleId",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cash_ledger",
                schema: "payments_cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    AmountDelta = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_ledger", x => x.Id);
                    table.CheckConstraint("CK_cash_ledger_amount_nonzero", "\"AmountDelta\" <> 0");
                    table.CheckConstraint("CK_cash_ledger_kind", "\"Kind\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_cash_ledger_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_ledger_cashier_shifts_CompanyId_CashierShiftId",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_ledger_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "confirmed_sales",
                schema: "pos_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PaymentRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmed_sales", x => x.Id);
                    table.UniqueConstraint("AK_confirmed_sales_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_confirmed_sales_total_positive", "\"Total\" > 0");
                    table.ForeignKey(
                        name: "FK_confirmed_sales_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirmed_sales_cashier_shifts_CompanyId_CashierShiftId",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirmed_sales_price_lists_CompanyId_PriceListId",
                        columns: x => new { x.CompanyId, x.PriceListId },
                        principalSchema: "catalog_pricing",
                        principalTable: "price_lists",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirmed_sales_sale_drafts_CompanyId_SourceDraftId",
                        columns: x => new { x.CompanyId, x.SourceDraftId },
                        principalSchema: "pos_sales",
                        principalTable: "sale_drafts",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirmed_sales_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "confirmed_sale_lines",
                schema: "pos_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SaleMode = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmed_sale_lines", x => x.Id);
                    table.CheckConstraint("CK_confirmed_sale_lines_quantity_positive", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_confirmed_sale_lines_unit_price_positive", "\"UnitPrice\" > 0");
                    table.ForeignKey(
                        name: "FK_confirmed_sale_lines_confirmed_sales_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "pos_sales",
                        principalTable: "confirmed_sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_confirmed_sale_lines_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_payments",
                schema: "payments_cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    TenderedAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_payments", x => x.Id);
                    table.CheckConstraint("CK_sale_payments_applied_positive", "\"AppliedAmount\" > 0");
                    table.CheckConstraint("CK_sale_payments_tendered_positive", "\"TenderedAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_sale_payments_confirmed_sales_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "pos_sales",
                        principalTable: "confirmed_sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CashierId",
                schema: "payments_cash",
                table: "cash_ledger",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CashierShiftId_Method_CreatedAtUtc",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CashLedgerShiftMethodCreatedColumns);

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CompanyId_BranchId_OperationId",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CashLedgerCompanyBranchOperationColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_ledger_CompanyId_CashierShiftId",
                schema: "payments_cash",
                table: "cash_ledger",
                columns: CashLedgerCompanyShiftColumns);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sale_lines_CompanyId_ProductId",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                columns: CompanyProductColumns);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sale_lines_SaleId",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CashierId",
                schema: "pos_sales",
                table: "confirmed_sales",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_BranchId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyBranchColumns);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_CashierShiftId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyShiftColumns);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_PriceListId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyPriceListColumns);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_SourceDraftId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyDraftColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_SaleId",
                schema: "payments_cash",
                table: "sale_payments",
                column: "SaleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_ledger",
                schema: "payments_cash");

            migrationBuilder.DropTable(
                name: "confirmed_sale_lines",
                schema: "pos_sales");

            migrationBuilder.DropTable(
                name: "sale_payments",
                schema: "payments_cash");

            migrationBuilder.DropTable(
                name: "confirmed_sales",
                schema: "pos_sales");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "ConfirmedSaleId",
                schema: "pos_sales",
                table: "sale_drafts");
        }
    }
}
