using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core scaffolding emits constant column arrays in migrations.

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReceiptDocumentsI18a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.CreateTable(
                name: "receipt_documents",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierIdentification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SourceSystem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DocumentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Destination = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanceledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanceledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_documents", x => x.Id);
                    table.UniqueConstraint("AK_receipt_documents_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_receipt_documents_decision", "(\"Status\" = 0 AND \"ApprovedByUserId\" IS NULL AND \"ApprovedAtUtc\" IS NULL AND \"CanceledByUserId\" IS NULL AND \"CanceledAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"ApprovedByUserId\" IS NOT NULL AND \"ApprovedAtUtc\" IS NOT NULL AND \"CanceledByUserId\" IS NULL AND \"CanceledAtUtc\" IS NULL) OR (\"Status\" = 2 AND \"ApprovedByUserId\" IS NULL AND \"ApprovedAtUtc\" IS NULL AND \"CanceledByUserId\" IS NOT NULL AND \"CanceledAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_receipt_documents_origin", "(\"SupplierId\" IS NOT NULL AND \"SourceSystem\" IS NULL AND \"SupplierCode\" <> '' AND \"SupplierName\" <> '') OR (\"SupplierId\" IS NULL AND \"SourceSystem\" IS NOT NULL AND \"SupplierCode\" = '' AND \"SupplierName\" = '')");
                    table.CheckConstraint("CK_receipt_documents_status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_receipt_documents_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_documents_suppliers_CompanyId_SupplierId",
                        columns: x => new { x.CompanyId, x.SupplierId },
                        principalSchema: "inventory",
                        principalTable: "suppliers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_documents_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_documents_users_CanceledByUserId",
                        column: x => x.CanceledByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_documents_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipt_lines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SaleMode = table.Column<int>(type: "integer", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    DifferenceStatus = table.Column<int>(type: "integer", nullable: false),
                    DifferenceResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DifferenceResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DifferenceReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    InventoryMovementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_lines", x => x.Id);
                    table.UniqueConstraint("AK_receipt_lines_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_receipt_lines_difference_status", "\"DifferenceStatus\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_receipt_lines_quantity", "\"ReceivedQuantity\" >= 0 AND (\"ExpectedQuantity\" IS NULL OR \"ExpectedQuantity\" >= 0)");
                    table.ForeignKey(
                        name: "FK_receipt_lines_inventory_movements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalSchema: "inventory",
                        principalTable: "inventory_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_receipt_documents_CompanyId_ReceiptId",
                        columns: x => new { x.CompanyId, x.ReceiptId },
                        principalSchema: "inventory",
                        principalTable: "receipt_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_users_DifferenceResolvedByUserId",
                        column: x => x.DifferenceResolvedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_documents_ApprovedByUserId",
                schema: "inventory",
                table: "receipt_documents",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_documents_CanceledByUserId",
                schema: "inventory",
                table: "receipt_documents",
                column: "CanceledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_documents_CompanyId_BranchId_OperationId",
                schema: "inventory",
                table: "receipt_documents",
                columns: new[] { "CompanyId", "BranchId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_documents_CompanyId_BranchId_Status_CreatedAtUtc",
                schema: "inventory",
                table: "receipt_documents",
                columns: new[] { "CompanyId", "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_documents_CompanyId_SupplierId",
                schema: "inventory",
                table: "receipt_documents",
                columns: new[] { "CompanyId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_documents_CreatedByUserId",
                schema: "inventory",
                table: "receipt_documents",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_CompanyId_ProductId",
                schema: "inventory",
                table: "receipt_lines",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_CompanyId_ReceiptId_ProductId",
                schema: "inventory",
                table: "receipt_lines",
                columns: new[] { "CompanyId", "ReceiptId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_DifferenceResolvedByUserId",
                schema: "inventory",
                table: "receipt_lines",
                column: "DifferenceResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_InventoryMovementId",
                schema: "inventory",
                table: "receipt_lines",
                column: "InventoryMovementId",
                unique: true,
                filter: "\"InventoryMovementId\" IS NOT NULL");

            migrationBuilder.Sql("""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES ('87f54e27-1f0d-47ca-bca5-ec0d4bb79eb8',
                    'inventory.receipts.manage',
                    'Registrar y confirmar recepciones de mercadería')
                ON CONFLICT ("Code") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Approved inventory receipts and their movements must be preserved during rollback.");
        }
    }
}
