using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasePackageCodesI20 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "catalog_pricing",
                table: "product_codes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "SaleAlternative");

            migrationBuilder.AddColumn<int>(
                name: "UnitsPerPackage",
                schema: "catalog_pricing",
                table: "product_codes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_product_codes_kind_factor",
                schema: "catalog_pricing",
                table: "product_codes",
                sql: "(\"Kind\" = 'SaleAlternative' AND \"UnitsPerPackage\" IS NULL) OR (\"Kind\" = 'PurchasePackage' AND \"UnitsPerPackage\" BETWEEN 2 AND 100000)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM catalog_pricing.product_codes
                               WHERE "Kind" = 'PurchasePackage') THEN
                        RAISE EXCEPTION 'Cannot remove I20 package codes while package definitions exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_product_codes_kind_factor",
                schema: "catalog_pricing",
                table: "product_codes");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "catalog_pricing",
                table: "product_codes");

            migrationBuilder.DropColumn(
                name: "UnitsPerPackage",
                schema: "catalog_pricing",
                table: "product_codes");
        }
    }
}
