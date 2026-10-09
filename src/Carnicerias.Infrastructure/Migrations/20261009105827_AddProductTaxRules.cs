using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductTaxRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_tax_rules",
                schema: "catalog_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Treatment = table.Column<int>(type: "integer", nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_tax_rules", x => x.Id);
                    table.CheckConstraint("CK_product_tax_rules_dates", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
                    table.CheckConstraint("CK_product_tax_rules_rate", "\"RatePercent\" >= 0 AND \"RatePercent\" <= 100");
                    table.CheckConstraint("CK_product_tax_rules_treatment", "\"Treatment\" IN (0, 1, 2) AND (\"Treatment\" = 0 OR \"RatePercent\" = 0)");
                    table.ForeignKey(
                        name: "FK_product_tax_rules_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_tax_rules_users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_tax_rules_ChangedByUserId",
                schema: "catalog_pricing",
                table: "product_tax_rules",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_product_tax_rules_CompanyId_ProductId_EffectiveFromUtc",
                schema: "catalog_pricing",
                table: "product_tax_rules",
                columns: ["CompanyId", "ProductId", "EffectiveFromUtc"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_tax_rules_CompanyId_ProductId_EffectiveToUtc",
                schema: "catalog_pricing",
                table: "product_tax_rules",
                columns: ["CompanyId", "ProductId", "EffectiveToUtc"],
                unique: true,
                filter: "\"EffectiveToUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_tax_rules",
                schema: "catalog_pricing");
        }
    }
}
