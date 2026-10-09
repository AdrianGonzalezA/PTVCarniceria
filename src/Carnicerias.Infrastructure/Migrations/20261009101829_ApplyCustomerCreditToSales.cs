using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyCustomerCreditToSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.AddColumn<decimal>(
                name: "CreditAppliedAmount",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "credit_applications",
                schema: "customers_credit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_applications", x => x.Id);
                    table.CheckConstraint("CK_credit_applications_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_credit_applications_cashier_shifts_CompanyId_CashierShiftId",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_applications_confirmed_sales_CompanyId_SaleId",
                        columns: x => new { x.CompanyId, x.SaleId },
                        principalSchema: "pos_sales",
                        principalTable: "confirmed_sales",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_applications_customers_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalSchema: "customers_credit",
                        principalTable: "customers",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_applications_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales",
                sql: "\"AccountChargeAmount\" >= 0 AND \"CreditAppliedAmount\" >= 0 AND \"AccountChargeAmount\" + \"CreditAppliedAmount\" <= \"Total\" AND (\"AccountChargeAmount\" + \"CreditAppliedAmount\" = 0 OR (\"CustomerId\" IS NOT NULL AND \"CustomerCode\" IS NOT NULL AND \"CustomerName\" IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_credit_applications_CashierId",
                schema: "customers_credit",
                table: "credit_applications",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_applications_CompanyId_CashierShiftId",
                schema: "customers_credit",
                table: "credit_applications",
                columns: ["CompanyId", "CashierShiftId"]);

            migrationBuilder.CreateIndex(
                name: "IX_credit_applications_CompanyId_CustomerId_CreatedAtUtc",
                schema: "customers_credit",
                table: "credit_applications",
                columns: ["CompanyId", "CustomerId", "CreatedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_credit_applications_CompanyId_SaleId",
                schema: "customers_credit",
                table: "credit_applications",
                columns: ["CompanyId", "SaleId"]);

            migrationBuilder.CreateIndex(
                name: "IX_credit_applications_SaleId",
                schema: "customers_credit",
                table: "credit_applications",
                column: "SaleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_applications",
                schema: "customers_credit");

            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "CreditAppliedAmount",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales",
                sql: "\"AccountChargeAmount\" >= 0 AND \"AccountChargeAmount\" <= \"Total\" AND (\"AccountChargeAmount\" = 0 OR (\"CustomerId\" IS NOT NULL AND \"CustomerCode\" IS NOT NULL AND \"CustomerName\" IS NOT NULL))");
        }
    }
}
