using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalDocumentAttempts : Migration
    {
        private static readonly string[] SalePrincipalColumns = ["CompanyId", "Id"];
        private static readonly string[] SaleColumns = ["CompanyId", "SaleId"];
        private static readonly string[] NumberColumns = ["IssuerCuit", "PointOfSale", "VoucherType", "Number"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fiscal_documents",
                schema: "pos_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuerCuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    PointOfSale = table.Column<int>(type: "integer", nullable: false),
                    VoucherType = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ReceiverDocumentType = table.Column<int>(type: "integer", nullable: false),
                    ReceiverDocumentNumber = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Cae = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    CaeExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCodes = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_documents", x => x.Id);
                    table.CheckConstraint("CK_fiscal_documents_authorization", "(\"Status\" = 3 AND \"Cae\" IS NOT NULL AND \"CaeExpiry\" IS NOT NULL AND \"AuthorizedAtUtc\" IS NOT NULL) OR (\"Status\" <> 3 AND \"Cae\" IS NULL AND \"CaeExpiry\" IS NULL AND \"AuthorizedAtUtc\" IS NULL)");
                    table.CheckConstraint("CK_fiscal_documents_status", "\"Status\" IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_fiscal_documents_confirmed_sales_CompanyId_SaleId",
                        columns: x => new { x.CompanyId, x.SaleId },
                        principalSchema: "pos_sales",
                        principalTable: "confirmed_sales",
                        principalColumns: SalePrincipalColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_documents_CompanyId_SaleId",
                schema: "pos_sales",
                table: "fiscal_documents",
                columns: SaleColumns,
                unique: true,
                filter: "\"Status\" <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_documents_IssuerCuit_PointOfSale_VoucherType_Number",
                schema: "pos_sales",
                table: "fiscal_documents",
                columns: NumberColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_documents",
                schema: "pos_sales");
        }
    }
}
