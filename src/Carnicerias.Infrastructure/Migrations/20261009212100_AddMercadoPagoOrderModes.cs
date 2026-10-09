using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMercadoPagoOrderModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "QrData",
                schema: "payments_cash",
                table: "point_payment_intents",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "ConfirmedSaleId",
                unique: true,
                filter: "\"ConfirmedSaleId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_payment_intents_mode",
                schema: "payments_cash",
                table: "point_payment_intents",
                sql: "\"Mode\" BETWEEN 0 AND 1");

            migrationBuilder.AddForeignKey(
                name: "FK_point_payment_intents_confirmed_sales_ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents",
                column: "ConfirmedSaleId",
                principalSchema: "pos_sales",
                principalTable: "confirmed_sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_point_payment_intents_confirmed_sales_ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_payment_intents_mode",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropColumn(
                name: "ConfirmedSaleId",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropColumn(
                name: "Mode",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.DropColumn(
                name: "QrData",
                schema: "payments_cash",
                table: "point_payment_intents");
        }
    }
}
