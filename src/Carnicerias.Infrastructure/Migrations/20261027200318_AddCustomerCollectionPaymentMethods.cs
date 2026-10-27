using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCollectionPaymentMethods : Migration
    {
        private static readonly string[] CollectionOperationIndexColumns =
            ["CompanyId", "CustomerCollectionOperationId"];
        private static readonly string[] DraftIndexColumns = ["CompanyId", "SaleDraftId"];
        private static readonly string[] ReceiptPaymentIndexColumns = ["ReceiptId", "Method"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.AlterColumn<Guid>(
                name: "SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedCollectionReceiptId",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerCollectionOperationId",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "collection_payments",
                schema: "customers_credit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    TenderedAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_payments", x => x.Id);
                    table.CheckConstraint("CK_collection_payments_applied_lte_tendered", "\"AppliedAmount\" <= \"TenderedAmount\"");
                    table.CheckConstraint("CK_collection_payments_applied_valid",
                        "\"AppliedAmount\" > 0 OR (\"Method\" = 0 AND \"AppliedAmount\" = 0)");
                    table.CheckConstraint("CK_collection_payments_tendered_positive", "\"TenderedAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_collection_payments_collection_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalSchema: "customers_credit",
                        principalTable: "collection_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO customers_credit.collection_payments
                    ("Id", "ReceiptId", "Method", "TenderedAmount", "AppliedAmount")
                SELECT gen_random_uuid(), "Id", "Method", "Amount", "Amount"
                FROM customers_credit.collection_receipts;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_CustomerCollectionOperation~",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: CollectionOperationIndexColumns,
                unique: true,
                filter: "\"CustomerCollectionOperationId\" IS NOT NULL AND \"Status\" IN (0, 1, 2, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: DraftIndexColumns,
                unique: true,
                filter: "\"SaleDraftId\" IS NOT NULL AND \"Status\" IN (0, 1, 2, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_ConfirmedCollectionReceiptId",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "ConfirmedCollectionReceiptId",
                unique: true,
                filter: "\"ConfirmedCollectionReceiptId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_payment_intents_owner",
                schema: "payments_cash",
                table: "point_payment_intents",
                sql: "(\"SaleDraftId\" IS NOT NULL AND \"CustomerCollectionOperationId\" IS NULL AND \"CustomerId\" IS NULL) OR (\"SaleDraftId\" IS NULL AND \"CustomerCollectionOperationId\" IS NOT NULL AND \"CustomerId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_collection_payments_ReceiptId_Method",
                schema: "customers_credit",
                table: "collection_payments",
                columns: ReceiptPaymentIndexColumns,
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_point_payment_intents_collection_receipts_ConfirmedCollecti~",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "ConfirmedCollectionReceiptId",
                principalSchema: "customers_credit",
                principalTable: "collection_receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM payments_cash.point_payment_intents
                               WHERE "CustomerCollectionOperationId" IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM customers_credit.collection_payments
                                  GROUP BY "ReceiptId"
                                  HAVING COUNT(*) > 1 OR BOOL_OR("TenderedAmount" <> "AppliedAmount"))
                    THEN RAISE EXCEPTION 'Cannot discard collection payment history';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_point_payment_intents_collection_receipts_ConfirmedCollecti~",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropTable(
                name: "collection_payments",
                schema: "customers_credit");

            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_CompanyId_CustomerCollectionOperation~",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_ConfirmedCollectionReceiptId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_payment_intents_owner",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropColumn(
                name: "ConfirmedCollectionReceiptId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropColumn(
                name: "CustomerCollectionOperationId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.AlterColumn<Guid>(
                name: "SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: DraftIndexColumns,
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 6)");
        }
    }
}
