using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Generated EF key and index arrays.

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptPackageReadsI20 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt_package_reads",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UnitsPerPackage = table.Column<int>(type: "integer", nullable: false),
                    PackageCount = table.Column<int>(type: "integer", nullable: false),
                    UnitQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReadByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_package_reads", x => x.Id);
                    table.CheckConstraint("CK_receipt_package_reads_quantity", "\"UnitsPerPackage\" BETWEEN 2 AND 100000 AND \"PackageCount\" BETWEEN 1 AND 100000 AND \"UnitQuantity\" BETWEEN 1 AND 100000 AND \"UnitQuantity\" = \"PackageCount\"::bigint * \"UnitsPerPackage\"");
                    table.ForeignKey(
                        name: "FK_receipt_package_reads_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_package_reads_receipt_documents_CompanyId_ReceiptId",
                        columns: x => new { x.CompanyId, x.ReceiptId },
                        principalSchema: "inventory",
                        principalTable: "receipt_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_package_reads_receipt_lines_CompanyId_LineId",
                        columns: x => new { x.CompanyId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "receipt_lines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_package_reads_users_ReadByUserId",
                        column: x => x.ReadByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_package_reads_CompanyId_LineId",
                schema: "inventory",
                table: "receipt_package_reads",
                columns: new[] { "CompanyId", "LineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_package_reads_CompanyId_ProductId",
                schema: "inventory",
                table: "receipt_package_reads",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_package_reads_CompanyId_ReceiptId",
                schema: "inventory",
                table: "receipt_package_reads",
                columns: new[] { "CompanyId", "ReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_package_reads_ReadByUserId",
                schema: "inventory",
                table: "receipt_package_reads",
                column: "ReadByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_package_reads_ReceiptId_OperationId",
                schema: "inventory",
                table: "receipt_package_reads",
                columns: new[] { "ReceiptId", "OperationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM inventory.receipt_package_reads) THEN
                        RAISE EXCEPTION 'Cannot remove I20 package reads while receipt history exists';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "receipt_package_reads",
                schema: "inventory");
        }
    }
}
