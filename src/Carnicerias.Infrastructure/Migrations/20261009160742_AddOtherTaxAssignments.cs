using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOtherTaxAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "other_tax_assignments",
                schema: "catalog_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxCatalogEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_other_tax_assignments", x => x.Id);
                    table.CheckConstraint("CK_other_tax_assignments_dates", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
                    table.ForeignKey(
                        name: "FK_other_tax_assignments_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_other_tax_assignments_tax_catalog_entries_CompanyId_TaxCata~",
                        columns: x => new { x.CompanyId, x.TaxCatalogEntryId },
                        principalSchema: "catalog_pricing",
                        principalTable: "tax_catalog_entries",
                        principalColumns: ["CompanyId", "Id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_other_tax_assignments_users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_other_tax_assignments_users_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_other_tax_assignments_AssignedByUserId",
                schema: "catalog_pricing",
                table: "other_tax_assignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_other_tax_assignments_CompanyId_ProductId_TaxCatalogEntryId~",
                schema: "catalog_pricing",
                table: "other_tax_assignments",
                columns: ["CompanyId", "ProductId", "TaxCatalogEntryId", "EffectiveToUtc"],
                unique: true,
                filter: "\"EffectiveToUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_other_tax_assignments_CompanyId_TaxCatalogEntryId_Effective~",
                schema: "catalog_pricing",
                table: "other_tax_assignments",
                columns: ["CompanyId", "TaxCatalogEntryId", "EffectiveToUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_other_tax_assignments_RemovedByUserId",
                schema: "catalog_pricing",
                table: "other_tax_assignments",
                column: "RemovedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "other_tax_assignments",
                schema: "catalog_pricing");
        }
    }
}
