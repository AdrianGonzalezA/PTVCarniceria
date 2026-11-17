using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductScalePlu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScalePlu",
                schema: "catalog_pricing",
                table: "products",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_CompanyId_ScalePlu",
                schema: "catalog_pricing",
                table: "products",
                columns: ["CompanyId", "ScalePlu"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_CompanyId_ScalePlu",
                schema: "catalog_pricing",
                table: "products");

            migrationBuilder.DropColumn(
                name: "ScalePlu",
                schema: "catalog_pricing",
                table: "products");
        }
    }
}
