using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaticMercadoPagoQr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_payment_intents_mode",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_payment_intents_mode",
                schema: "payments_cash",
                table: "point_payment_intents",
                sql: "\"Mode\" BETWEEN 0 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_payment_intents_mode",
                schema: "payments_cash",
                table: "point_payment_intents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_payment_intents_mode",
                schema: "payments_cash",
                table: "point_payment_intents",
                sql: "\"Mode\" BETWEEN 0 AND 1");
        }
    }
}
