using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF-generated migration arrays are immutable in use.

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferReceiptI18b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pieces_CompanyId_NormalizedSourceSystem_ExternalIdentifier",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.AddColumn<Guid>(
                name: "OriginPieceId",
                schema: "inventory",
                table: "pieces",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "transfer_receipt_documents",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceCompanyCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceBranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceBranchKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceBranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTransferId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DocumentKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ImportedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_transfer_receipt_documents", x => x.Id);
                    table.UniqueConstraint("AK_transfer_receipt_documents_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_transfer_receipt_documents_decision", "(\"Status\" = 0 AND \"ApprovedByUserId\" IS NULL AND \"ApprovedAtUtc\" IS NULL AND \"CanceledByUserId\" IS NULL AND \"CanceledAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"ApprovedByUserId\" IS NOT NULL AND \"ApprovedAtUtc\" IS NOT NULL AND \"CanceledByUserId\" IS NULL AND \"CanceledAtUtc\" IS NULL) OR (\"Status\" = 2 AND \"ApprovedByUserId\" IS NULL AND \"ApprovedAtUtc\" IS NULL AND \"CanceledByUserId\" IS NOT NULL AND \"CanceledAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_transfer_receipt_documents_status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_transfer_receipt_documents_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipt_documents_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipt_documents_users_CanceledByUserId",
                        column: x => x.CanceledByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipt_documents_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_receipt_lines",
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
                    SentQuantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    IsPiece = table.Column<bool>(type: "boolean", nullable: false),
                    SourceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePieceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceSystem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SourceExternalIdentifier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SourceRawBarcode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    BarcodeProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReadSourceSystem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IdentifierField = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ExternalIdentifier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RawBarcode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SourcePieceVerified = table.Column<bool>(type: "boolean", nullable: false),
                    InventoryMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    InventoryPieceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DifferenceStatus = table.Column<int>(type: "integer", nullable: false),
                    DifferenceReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    DifferenceResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DifferenceResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_receipt_lines", x => x.Id);
                    table.CheckConstraint("CK_transfer_receipt_lines_difference_status", "\"DifferenceStatus\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_transfer_receipt_lines_piece", "(\"IsPiece\" = false AND \"SourcePieceId\" IS NULL AND \"BarcodeProfileId\" IS NULL AND \"ExternalIdentifier\" IS NULL) OR (\"IsPiece\" = true AND \"SaleMode\" = 0 AND (\"ReceivedQuantity\" = 0 OR (\"BarcodeProfileId\" IS NOT NULL AND \"ExternalIdentifier\" IS NOT NULL AND \"RawBarcode\" IS NOT NULL)))");
                    table.CheckConstraint("CK_transfer_receipt_lines_quantity", "\"SentQuantity\" BETWEEN 0 AND 100000 AND \"ReceivedQuantity\" BETWEEN 0 AND 100000 AND (\"SaleMode\" <> 1 OR (\"SentQuantity\" = trunc(\"SentQuantity\") AND \"ReceivedQuantity\" = trunc(\"ReceivedQuantity\")))");
                    table.CheckConstraint("CK_transfer_receipt_lines_verified", "\"SourcePieceVerified\" = false OR (\"IsPiece\" = true AND \"SourcePieceId\" IS NOT NULL AND \"BarcodeProfileId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_transfer_receipt_lines_barcode_profiles_CompanyId_BarcodePr~",
                        columns: x => new { x.CompanyId, x.BarcodeProfileId },
                        principalSchema: "inventory",
                        principalTable: "barcode_profiles",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipt_lines_inventory_movements_InventoryMovemen~",
                        column: x => x.InventoryMovementId,
                        principalSchema: "inventory",
                        principalTable: "inventory_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipt_lines_pieces_InventoryPieceId",
                        column: x => x.InventoryPieceId,
                        principalSchema: "inventory",
                        principalTable: "pieces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipt_lines_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipt_lines_transfer_receipt_documents_CompanyId~",
                        columns: x => new { x.CompanyId, x.ReceiptId },
                        principalSchema: "inventory",
                        principalTable: "transfer_receipt_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipt_lines_users_DifferenceResolvedByUserId",
                        column: x => x.DifferenceResolvedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_NormalizedSourceSystem_ExternalIdentifier",
                schema: "inventory",
                table: "pieces",
                columns: new[] { "CompanyId", "NormalizedSourceSystem", "ExternalIdentifier" },
                unique: true,
                filter: "\"Status\" <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_OriginPieceId",
                schema: "inventory",
                table: "pieces",
                columns: new[] { "CompanyId", "OriginPieceId" },
                unique: true,
                filter: "\"OriginPieceId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_documents_ApprovedByUserId",
                schema: "inventory",
                table: "transfer_receipt_documents",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_documents_CanceledByUserId",
                schema: "inventory",
                table: "transfer_receipt_documents",
                column: "CanceledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_documents_CompanyId_BranchId_OperationId",
                schema: "inventory",
                table: "transfer_receipt_documents",
                columns: new[] { "CompanyId", "BranchId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_documents_CompanyId_BranchId_SourceBranchK~",
                schema: "inventory",
                table: "transfer_receipt_documents",
                columns: new[] { "CompanyId", "BranchId", "SourceBranchKey", "DocumentKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_documents_CompanyId_BranchId_SourceTransfe~",
                schema: "inventory",
                table: "transfer_receipt_documents",
                columns: new[] { "CompanyId", "BranchId", "SourceTransferId" },
                unique: true,
                filter: "\"SourceTransferId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_documents_CompanyId_BranchId_Status_Create~",
                schema: "inventory",
                table: "transfer_receipt_documents",
                columns: new[] { "CompanyId", "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_documents_CreatedByUserId",
                schema: "inventory",
                table: "transfer_receipt_documents",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_lines_CompanyId_BarcodeProfileId",
                schema: "inventory",
                table: "transfer_receipt_lines",
                columns: new[] { "CompanyId", "BarcodeProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_lines_CompanyId_ProductId",
                schema: "inventory",
                table: "transfer_receipt_lines",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_lines_CompanyId_ReceiptId_SourceLineId",
                schema: "inventory",
                table: "transfer_receipt_lines",
                columns: new[] { "CompanyId", "ReceiptId", "SourceLineId" },
                unique: true,
                filter: "\"SourceLineId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_lines_DifferenceResolvedByUserId",
                schema: "inventory",
                table: "transfer_receipt_lines",
                column: "DifferenceResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_lines_InventoryMovementId",
                schema: "inventory",
                table: "transfer_receipt_lines",
                column: "InventoryMovementId",
                unique: true,
                filter: "\"InventoryMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipt_lines_InventoryPieceId",
                schema: "inventory",
                table: "transfer_receipt_lines",
                column: "InventoryPieceId",
                unique: true,
                filter: "\"InventoryPieceId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM inventory.transfer_receipt_documents) OR
                       EXISTS (SELECT 1 FROM inventory.inventory_movements WHERE "Kind" = 6) OR
                       EXISTS (SELECT 1 FROM inventory.pieces WHERE "OriginPieceId" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'I18b has transfer receipts or movements; preserve the audit trail';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "transfer_receipt_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "transfer_receipt_documents",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_pieces_CompanyId_NormalizedSourceSystem_ExternalIdentifier",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropIndex(
                name: "IX_pieces_CompanyId_OriginPieceId",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "OriginPieceId",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_NormalizedSourceSystem_ExternalIdentifier",
                schema: "inventory",
                table: "pieces",
                columns: new[] { "CompanyId", "NormalizedSourceSystem", "ExternalIdentifier" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5)");
        }
    }
}
