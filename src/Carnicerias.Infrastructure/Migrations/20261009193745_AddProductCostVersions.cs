using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCostVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_cost_versions",
                schema: "catalog_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_cost_versions", x => x.Id);
                    table.CheckConstraint("CK_product_cost_versions_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_product_cost_versions_effective_range", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
                    table.ForeignKey(
                        name: "FK_product_cost_versions_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_cost_versions_users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_cost_versions_ChangedByUserId",
                schema: "catalog_pricing",
                table: "product_cost_versions",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_product_cost_versions_CompanyId_ProductId_EffectiveFromUtc",
                schema: "catalog_pricing",
                table: "product_cost_versions",
                columns: ["CompanyId", "ProductId", "EffectiveFromUtc"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_cost_versions_CompanyId_ProductId_EffectiveToUtc",
                schema: "catalog_pricing",
                table: "product_cost_versions",
                columns: ["CompanyId", "ProductId", "EffectiveToUtc"],
                unique: true,
                filter: "\"EffectiveToUtc\" IS NULL");

            // The earlier schema retained only current cost; its unknown past cannot be reconstructed.
            migrationBuilder.Sql("""
                INSERT INTO catalog_pricing.product_cost_versions
                    ("Id", "CompanyId", "ProductId", "Amount", "EffectiveFromUtc", "ChangedByUserId")
                SELECT gen_random_uuid(), "CompanyId", "Id", "Cost", clock_timestamp(), NULL
                FROM catalog_pricing.products;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_cost_versions",
                schema: "catalog_pricing");
        }
    }
}
