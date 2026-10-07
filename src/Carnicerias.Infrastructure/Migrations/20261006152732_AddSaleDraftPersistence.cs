using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleDraftPersistence : Migration
    {
        private static readonly string[] CompanyIdAndIdColumns = ["CompanyId", "Id"];
        private static readonly string[] DraftProductColumns = ["CompanyId", "ProductId"];
        private static readonly string[] BranchUserColumns = ["CompanyId", "BranchId", "UserId"];
        private static readonly string[] CompanyPriceListColumns = ["CompanyId", "PriceListId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pos_sales");

            migrationBuilder.CreateTable(
                name: "sale_drafts",
                schema: "pos_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_drafts", x => x.Id);
                    table.UniqueConstraint("AK_sale_drafts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sale_drafts_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyIdAndIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_drafts_price_lists_CompanyId_PriceListId",
                        columns: x => new { x.CompanyId, x.PriceListId },
                        principalSchema: "catalog_pricing",
                        principalTable: "price_lists",
                        principalColumns: CompanyIdAndIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_drafts_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_draft_lines",
                schema: "pos_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SaleMode = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_draft_lines", x => x.Id);
                    table.CheckConstraint("CK_sale_draft_lines_price_positive", "\"UnitPrice\" > 0");
                    table.CheckConstraint("CK_sale_draft_lines_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_sale_draft_lines_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: CompanyIdAndIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_draft_lines_sale_drafts_SaleDraftId",
                        column: x => x.SaleDraftId,
                        principalSchema: "pos_sales",
                        principalTable: "sale_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_lines_CompanyId_ProductId",
                schema: "pos_sales",
                table: "sale_draft_lines",
                columns: DraftProductColumns);

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_lines_SaleDraftId",
                schema: "pos_sales",
                table: "sale_draft_lines",
                column: "SaleDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: BranchUserColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CompanyId_PriceListId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: CompanyPriceListColumns);

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_UserId",
                schema: "pos_sales",
                table: "sale_drafts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_draft_lines",
                schema: "pos_sales");

            migrationBuilder.DropTable(
                name: "sale_drafts",
                schema: "pos_sales");
        }
    }
}
