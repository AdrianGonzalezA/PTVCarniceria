using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_sale_charges_CompanyId_CustomerId_SaleId",
                schema: "customers_credit",
                table: "sale_charges",
                columns: ["CompanyId", "CustomerId", "SaleId"]);

            migrationBuilder.CreateTable(
                name: "collection_receipts",
                schema: "customers_credit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    PosTerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_receipts", x => x.Id);
                    table.UniqueConstraint("AK_collection_receipts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_collection_receipts_amounts", "\"Amount\" > 0 AND \"CreditAmount\" >= 0 AND \"CreditAmount\" <= \"Amount\"");
                    table.ForeignKey(
                        name: "FK_collection_receipts_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_receipts_cashier_shifts_CompanyId_CashierShiftId",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_receipts_customers_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalSchema: "customers_credit",
                        principalTable: "customers",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_receipts_pos_terminals_CompanyId_BranchId_PosTer~",
                        columns: x => new { x.CompanyId, x.BranchId, x.PosTerminalId },
                        principalSchema: "platform_access",
                        principalTable: "pos_terminals",
                        principalColumns: ["CompanyId", "BranchId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_receipts_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_allocations",
                schema: "customers_credit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_allocations", x => x.Id);
                    table.CheckConstraint("CK_collection_allocations_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_collection_allocations_collection_receipts_CompanyId_Receip~",
                        columns: x => new { x.CompanyId, x.ReceiptId },
                        principalSchema: "customers_credit",
                        principalTable: "collection_receipts",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_allocations_sale_charges_CompanyId_CustomerId_Sa~",
                        columns: x => new { x.CompanyId, x.CustomerId, x.SaleId },
                        principalSchema: "customers_credit",
                        principalTable: "sale_charges",
                        principalColumns: ["CompanyId", "CustomerId", "SaleId"],
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger",
                sql: "\"Kind\" IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_collection_allocations_CompanyId_CustomerId_SaleId",
                schema: "customers_credit",
                table: "collection_allocations",
                columns: ["CompanyId", "CustomerId", "SaleId"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_allocations_CompanyId_ReceiptId",
                schema: "customers_credit",
                table: "collection_allocations",
                columns: ["CompanyId", "ReceiptId"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_allocations_ReceiptId_SaleId",
                schema: "customers_credit",
                table: "collection_allocations",
                columns: ["ReceiptId", "SaleId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_receipts_CashierId",
                schema: "customers_credit",
                table: "collection_receipts",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_collection_receipts_CompanyId_BranchId_PosTerminalId",
                schema: "customers_credit",
                table: "collection_receipts",
                columns: ["CompanyId", "BranchId", "PosTerminalId"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_receipts_CompanyId_CashierShiftId",
                schema: "customers_credit",
                table: "collection_receipts",
                columns: ["CompanyId", "CashierShiftId"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_receipts_CompanyId_CustomerId_CreatedAtUtc",
                schema: "customers_credit",
                table: "collection_receipts",
                columns: ["CompanyId", "CustomerId", "CreatedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_collection_receipts_CompanyId_OperationId",
                schema: "customers_credit",
                table: "collection_receipts",
                columns: ["CompanyId", "OperationId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_receipts_ReceiptNumber",
                schema: "customers_credit",
                table: "collection_receipts",
                column: "ReceiptNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_allocations",
                schema: "customers_credit");

            migrationBuilder.DropTable(
                name: "collection_receipts",
                schema: "customers_credit");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_sale_charges_CompanyId_CustomerId_SaleId",
                schema: "customers_credit",
                table: "sale_charges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cash_ledger_kind",
                schema: "payments_cash",
                table: "cash_ledger",
                sql: "\"Kind\" IN (0, 1, 2)");
        }
    }
}
