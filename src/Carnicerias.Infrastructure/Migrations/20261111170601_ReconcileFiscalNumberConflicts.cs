using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileFiscalNumberConflicts : Migration
    {
        private static readonly string[] NumberColumns =
            ["IssuerCuit", "PointOfSale", "VoucherType", "Number"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiscal_documents_IssuerCuit_PointOfSale_VoucherType_Number",
                schema: "pos_sales",
                table: "fiscal_documents");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_documents_IssuerCuit_PointOfSale_VoucherType_Number",
                schema: "pos_sales",
                table: "fiscal_documents",
                columns: NumberColumns,
                unique: true,
                filter: "\"Status\" <> 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pos_sales.fiscal_documents
                        GROUP BY "IssuerCuit", "PointOfSale", "VoucherType", "Number"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Fiscal numbering history contains reused numbers; reverting this index would discard valid history.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_fiscal_documents_IssuerCuit_PointOfSale_VoucherType_Number",
                schema: "pos_sales",
                table: "fiscal_documents");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_documents_IssuerCuit_PointOfSale_VoucherType_Number",
                schema: "pos_sales",
                table: "fiscal_documents",
                columns: NumberColumns,
                unique: true);
        }
    }
}
