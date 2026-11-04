using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalDocumentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fiscal_document_snapshots",
                schema: "pos_sales",
                columns: table => new
                {
                    FiscalDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiverVatConditionCode = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TaxableBase = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ExemptAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    NotTaxedAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_document_snapshots", x => x.FiscalDocumentId);
                    table.CheckConstraint("CK_fiscal_document_snapshots_amounts", "\"Total\" > 0 AND \"TaxableBase\" >= 0 AND \"VatAmount\" >= 0 AND \"ExemptAmount\" >= 0 AND \"NotTaxedAmount\" >= 0 AND \"ReceiverVatConditionCode\" > 0 AND \"Total\" = \"TaxableBase\" + \"VatAmount\" + \"ExemptAmount\" + \"NotTaxedAmount\"");
                    table.ForeignKey(
                        name: "FK_fiscal_document_snapshots_fiscal_documents_FiscalDocumentId",
                        column: x => x.FiscalDocumentId,
                        principalSchema: "pos_sales",
                        principalTable: "fiscal_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_document_vat_lines",
                schema: "pos_sales",
                columns: table => new
                {
                    FiscalDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArcaRateCode = table.Column<int>(type: "integer", nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxableBase = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_document_vat_lines", x => new { x.FiscalDocumentId, x.ArcaRateCode });
                    table.CheckConstraint("CK_fiscal_document_vat_lines_amounts", "\"ArcaRateCode\" > 0 AND \"RatePercent\" >= 0 AND \"TaxableBase\" > 0 AND \"TaxAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_fiscal_document_vat_lines_fiscal_document_snapshots_FiscalD~",
                        column: x => x.FiscalDocumentId,
                        principalSchema: "pos_sales",
                        principalTable: "fiscal_document_snapshots",
                        principalColumn: "FiscalDocumentId",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pos_sales.fiscal_document_snapshots) THEN
                        RAISE EXCEPTION 'Cannot remove fiscal snapshots after issuance attempts were recorded.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "fiscal_document_vat_lines",
                schema: "pos_sales");

            migrationBuilder.DropTable(
                name: "fiscal_document_snapshots",
                schema: "pos_sales");
        }
    }
}
