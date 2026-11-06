using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF-generated migration arrays are immutable in use.

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnatomicalGroupI22 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnatomicalGroup",
                schema: "catalog_pricing",
                table: "products",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnatomicalGroupNormalized",
                schema: "catalog_pricing",
                table: "products",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_CompanyId_AnatomicalGroupNormalized",
                schema: "catalog_pricing",
                table: "products",
                columns: new[] { "CompanyId", "AnatomicalGroupNormalized" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_anatomical_group",
                schema: "catalog_pricing",
                table: "products",
                sql: "(\"AnatomicalGroup\" IS NULL) = (\"AnatomicalGroupNormalized\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM catalog_pricing.products
                             WHERE "AnatomicalGroup" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot remove anatomical groups while products are classified';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_products_CompanyId_AnatomicalGroupNormalized",
                schema: "catalog_pricing",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_anatomical_group",
                schema: "catalog_pricing",
                table: "products");

            migrationBuilder.DropColumn(
                name: "AnatomicalGroup",
                schema: "catalog_pricing",
                table: "products");

            migrationBuilder.DropColumn(
                name: "AnatomicalGroupNormalized",
                schema: "catalog_pricing",
                table: "products");
        }
    }
}
