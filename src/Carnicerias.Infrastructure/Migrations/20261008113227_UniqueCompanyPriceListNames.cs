using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueCompanyPriceListNames : Migration
    {
        private static readonly string[] CompanyNameColumns = ["CompanyId", "Name"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_price_lists_CompanyId_Name",
                schema: "catalog_pricing",
                table: "price_lists",
                columns: CompanyNameColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_price_lists_CompanyId_Name",
                schema: "catalog_pricing",
                table: "price_lists");
        }
    }
}
