using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalePaymentInstrumentDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailsJson",
                schema: "payments_cash",
                table: "sale_payment_instruments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM payments_cash.sale_payment_instruments
                               WHERE "DetailsJson" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot drop payment instrument details while recorded details exist.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "DetailsJson",
                schema: "payments_cash",
                table: "sale_payment_instruments");
        }
    }
}
