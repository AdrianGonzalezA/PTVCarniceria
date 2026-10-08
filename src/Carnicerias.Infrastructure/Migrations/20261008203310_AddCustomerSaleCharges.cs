using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSaleCharges : Migration
    {
        private const string AccountChargePermissionId = "f1460b6c-7a0d-4a3a-92a5-4546a5f1a728";
        private static readonly string[] CompanyAndId = ["CompanyId", "Id"];
        private static readonly string[] CompanyAndBranchId = ["CompanyId", "BranchId"];
        private static readonly string[] CompanyAndCustomerId = ["CompanyId", "CustomerId"];
        private static readonly string[] CompanyAndShiftId = ["CompanyId", "CashierShiftId"];
        private static readonly string[] CompanyAndSaleId = ["CompanyId", "SaleId"];
        private static readonly string[] CompanyCustomerCreated = ["CompanyId", "CustomerId", "CreatedAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AccountChargeAmount",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sale_charges",
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
                    table.PrimaryKey("PK_sale_charges", x => x.Id);
                    table.CheckConstraint("CK_sale_charges_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_sale_charges_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyAndId,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_charges_cashier_shifts_CompanyId_CashierShiftId",
                        columns: x => new { x.CompanyId, x.CashierShiftId },
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumns: CompanyAndId,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_charges_confirmed_sales_CompanyId_SaleId",
                        columns: x => new { x.CompanyId, x.SaleId },
                        principalSchema: "pos_sales",
                        principalTable: "confirmed_sales",
                        principalColumns: CompanyAndId,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_charges_customers_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalSchema: "customers_credit",
                        principalTable: "customers",
                        principalColumns: CompanyAndId,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_charges_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_CustomerId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyAndCustomerId);

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales",
                sql: "\"AccountChargeAmount\" >= 0 AND \"AccountChargeAmount\" <= \"Total\" AND (\"AccountChargeAmount\" = 0 OR \"CustomerId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_sale_charges_CashierId",
                schema: "customers_credit",
                table: "sale_charges",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_charges_CompanyId_BranchId",
                schema: "customers_credit",
                table: "sale_charges",
                columns: CompanyAndBranchId);

            migrationBuilder.CreateIndex(
                name: "IX_sale_charges_CompanyId_CashierShiftId",
                schema: "customers_credit",
                table: "sale_charges",
                columns: CompanyAndShiftId);

            migrationBuilder.CreateIndex(
                name: "IX_sale_charges_CompanyId_CustomerId_CreatedAtUtc",
                schema: "customers_credit",
                table: "sale_charges",
                columns: CompanyCustomerCreated);

            migrationBuilder.CreateIndex(
                name: "IX_sale_charges_CompanyId_SaleId",
                schema: "customers_credit",
                table: "sale_charges",
                columns: CompanyAndSaleId);

            migrationBuilder.CreateIndex(
                name: "IX_sale_charges_SaleId",
                schema: "customers_credit",
                table: "sale_charges",
                column: "SaleId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_sales_customers_CompanyId_CustomerId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: CompanyAndCustomerId,
                principalSchema: "customers_credit",
                principalTable: "customers",
                principalColumns: CompanyAndId,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql($$"""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES ('{{AccountChargePermissionId}}', 'pos.account.charge',
                    'Cargar el saldo de una venta a la cuenta corriente de un cliente')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" IN ('administrator', 'cashier')
                    AND permission."Code" = 'pos.account.charge'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DELETE FROM platform_access.role_permissions
                WHERE "PermissionId" = '{{AccountChargePermissionId}}';
                DELETE FROM platform_access.permissions
                WHERE "Id" = '{{AccountChargePermissionId}}';
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_confirmed_sales_customers_CompanyId_CustomerId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropTable(
                name: "sale_charges",
                schema: "customers_credit");

            migrationBuilder.DropIndex(
                name: "IX_confirmed_sales_CompanyId_CustomerId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_sales_account_charge",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "AccountChargeAmount",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "pos_sales",
                table: "confirmed_sales");
        }
    }
}
