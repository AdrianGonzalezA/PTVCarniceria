using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueActiveMercadoPagoOrderPerDraft : Migration
    {
        private static readonly string[] ActiveDraftColumns = ["CompanyId", "SaleDraftId"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents",
                columns: ActiveDraftColumns,
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_point_payment_intents_CompanyId_SaleDraftId",
                schema: "payments_cash",
                table: "point_payment_intents");
        }
    }
}
