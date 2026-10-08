using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleTicketSlots : Migration
    {
        private static readonly string[] ActiveDraftSlotColumns =
            ["CompanyId", "BranchId", "UserId", "TicketSlot"];
        private static readonly string[] LegacyActiveDraftColumns =
            ["CompanyId", "BranchId", "UserId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.AddColumn<string>(
                name: "TicketSlot",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "A");

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId_TicketSlot",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: ActiveDraftSlotColumns,
                unique: true,
                filter: "\"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId_TicketSlot",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "TicketSlot",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: LegacyActiveDraftColumns,
                unique: true,
                filter: "\"Status\" = 0");
        }
    }
}
