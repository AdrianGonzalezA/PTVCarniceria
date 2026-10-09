using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkExistingVatRulesToTaxCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TaxCatalogEntryId",
                schema: "catalog_pricing",
                table: "product_tax_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO catalog_pricing.tax_catalog_entries
                    ("Id", "CompanyId", "Code", "Name", "Kind", "RatePercent", "IsActive",
                     "CreatedByUserId", "CreatedAtUtc")
                SELECT gen_random_uuid(), source."CompanyId",
                       'IVA_' || replace(source."RatePercent"::numeric(5,2)::text, '.', '_'),
                       'IVA ' || source."RatePercent"::numeric(5,2)::text || ' %',
                       0, source."RatePercent", TRUE,
                       source."ChangedByUserId", source."EffectiveFromUtc"
                FROM (
                    SELECT DISTINCT ON ("CompanyId", "RatePercent")
                           "CompanyId", "RatePercent", "ChangedByUserId", "EffectiveFromUtc"
                    FROM catalog_pricing.product_tax_rules
                    WHERE "Treatment" = 0
                    ORDER BY "CompanyId", "RatePercent", "EffectiveFromUtc", "Id"
                ) source
                ON CONFLICT ("CompanyId", "Code") DO NOTHING;

                UPDATE catalog_pricing.product_tax_rules rule
                SET "TaxCatalogEntryId" = tax."Id"
                FROM catalog_pricing.tax_catalog_entries tax
                WHERE rule."CompanyId" = tax."CompanyId"
                  AND rule."Treatment" = 0
                  AND rule."RatePercent" = tax."RatePercent"
                  AND tax."Kind" = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_product_tax_rules_CompanyId_TaxCatalogEntryId",
                schema: "catalog_pricing",
                table: "product_tax_rules",
                columns: ["CompanyId", "TaxCatalogEntryId"]);

            migrationBuilder.AddCheckConstraint(
                name: "CK_product_tax_rules_catalog_treatment",
                schema: "catalog_pricing",
                table: "product_tax_rules",
                sql: "\"TaxCatalogEntryId\" IS NULL OR \"Treatment\" = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_product_tax_rules_tax_catalog_entries_CompanyId_TaxCatalogE~",
                schema: "catalog_pricing",
                table: "product_tax_rules",
                columns: ["CompanyId", "TaxCatalogEntryId"],
                principalSchema: "catalog_pricing",
                principalTable: "tax_catalog_entries",
                principalColumns: ["CompanyId", "Id"],
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_product_tax_rules_tax_catalog_entries_CompanyId_TaxCatalogE~",
                schema: "catalog_pricing",
                table: "product_tax_rules");

            migrationBuilder.DropIndex(
                name: "IX_product_tax_rules_CompanyId_TaxCatalogEntryId",
                schema: "catalog_pricing",
                table: "product_tax_rules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_product_tax_rules_catalog_treatment",
                schema: "catalog_pricing",
                table: "product_tax_rules");

            migrationBuilder.DropColumn(
                name: "TaxCatalogEntryId",
                schema: "catalog_pricing",
                table: "product_tax_rules");
        }
    }
}
