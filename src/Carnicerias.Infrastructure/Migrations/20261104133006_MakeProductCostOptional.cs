using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeProductCostOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Cost",
                schema: "catalog_pricing",
                table: "products",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM catalog_pricing.products WHERE "Cost" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot make product cost required while unknown costs exist.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<decimal>(
                name: "Cost",
                schema: "catalog_pricing",
                table: "products",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
