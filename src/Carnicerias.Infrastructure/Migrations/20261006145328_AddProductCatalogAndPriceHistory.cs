using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCatalogAndPriceHistory : Migration
    {
        private static readonly string[] CompanyIdColumns = ["CompanyId", "Id"];
        private static readonly string[] CategoryNameIndexColumns = ["CompanyId", "Name"];
        private static readonly string[] ProductCodeIndexColumns = ["CompanyId", "NormalizedCode"];
        private static readonly string[] ProductCompanyIndexColumns = ["CompanyId", "ProductId"];
        private static readonly string[] PriceProductIndexColumns = ["CompanyId", "PriceListId", "ProductId"];
        private static readonly string[] PriceEffectiveIndexColumns = ["CompanyId", "PriceListId", "ProductId", "EffectiveFromUtc"];
        private static readonly string[] ProductCategoryIndexColumns = ["CompanyId", "CategoryId", "IsActive"];
        private static readonly string[] ProductNormalizedCodeIndexColumns = ["CompanyId", "NormalizedCode"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                schema: "catalog_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                    table.UniqueConstraint("AK_categories_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_categories_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NormalizedCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SaleMode = table.Column<int>(type: "integer", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                    table.UniqueConstraint("AK_products_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_products_cost_positive", "\"Cost\" > 0");
                    table.CheckConstraint("CK_products_sale_mode", "\"SaleMode\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_products_categories_CompanyId_CategoryId",
                        columns: x => new { x.CompanyId, x.CategoryId },
                        principalSchema: "catalog_pricing",
                        principalTable: "categories",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_products_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_codes",
                schema: "catalog_pricing",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_codes", x => new { x.ProductId, x.NormalizedCode });
                    table.ForeignKey(
                        name: "FK_product_codes_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_prices",
                schema: "catalog_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_prices", x => x.Id);
                    table.CheckConstraint("CK_product_prices_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_product_prices_effective_range", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
                    table.ForeignKey(
                        name: "FK_product_prices_price_lists_CompanyId_PriceListId",
                        columns: x => new { x.CompanyId, x.PriceListId },
                        principalSchema: "catalog_pricing",
                        principalTable: "price_lists",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_prices_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_prices_users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_CompanyId_Name",
                schema: "catalog_pricing",
                table: "categories",
                columns: CategoryNameIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_codes_CompanyId_NormalizedCode",
                schema: "catalog_pricing",
                table: "product_codes",
                columns: ProductCodeIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_codes_CompanyId_ProductId",
                schema: "catalog_pricing",
                table: "product_codes",
                columns: ProductCompanyIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_ChangedByUserId",
                schema: "catalog_pricing",
                table: "product_prices",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_CompanyId_PriceListId_ProductId",
                schema: "catalog_pricing",
                table: "product_prices",
                columns: PriceProductIndexColumns,
                unique: true,
                filter: "\"EffectiveToUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_CompanyId_PriceListId_ProductId_EffectiveFro~",
                schema: "catalog_pricing",
                table: "product_prices",
                columns: PriceEffectiveIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_prices_CompanyId_ProductId",
                schema: "catalog_pricing",
                table: "product_prices",
                columns: ProductCompanyIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_products_CompanyId_CategoryId_IsActive",
                schema: "catalog_pricing",
                table: "products",
                columns: ProductCategoryIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_products_CompanyId_NormalizedCode",
                schema: "catalog_pricing",
                table: "products",
                columns: ProductNormalizedCodeIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_codes",
                schema: "catalog_pricing");

            migrationBuilder.DropTable(
                name: "product_prices",
                schema: "catalog_pricing");

            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog_pricing");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog_pricing");
        }
    }
}
