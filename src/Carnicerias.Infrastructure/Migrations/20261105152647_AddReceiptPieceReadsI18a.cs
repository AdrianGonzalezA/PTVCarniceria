using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Generated EF key and index arrays.

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptPieceReadsI18a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_barcode_profiles_CompanyId_Id",
                schema: "inventory",
                table: "barcode_profiles",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "receipt_piece_reads",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BarcodeProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedSourceSystem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IdentifierField = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalIdentifier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RawBarcode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    InventoryPieceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_piece_reads", x => x.Id);
                    table.CheckConstraint("CK_receipt_piece_reads_weight_positive", "\"WeightKg\" > 0");
                    table.ForeignKey(
                        name: "FK_receipt_piece_reads_barcode_profiles_CompanyId_BarcodeProfi~",
                        columns: x => new { x.CompanyId, x.BarcodeProfileId },
                        principalSchema: "inventory",
                        principalTable: "barcode_profiles",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_piece_reads_pieces_InventoryPieceId",
                        column: x => x.InventoryPieceId,
                        principalSchema: "inventory",
                        principalTable: "pieces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_piece_reads_receipt_documents_CompanyId_ReceiptId",
                        columns: x => new { x.CompanyId, x.ReceiptId },
                        principalSchema: "inventory",
                        principalTable: "receipt_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_piece_reads_receipt_lines_CompanyId_LineId",
                        columns: x => new { x.CompanyId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "receipt_lines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_piece_reads_CompanyId_BarcodeProfileId",
                schema: "inventory",
                table: "receipt_piece_reads",
                columns: new[] { "CompanyId", "BarcodeProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_piece_reads_CompanyId_LineId",
                schema: "inventory",
                table: "receipt_piece_reads",
                columns: new[] { "CompanyId", "LineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_piece_reads_CompanyId_ReceiptId",
                schema: "inventory",
                table: "receipt_piece_reads",
                columns: new[] { "CompanyId", "ReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_piece_reads_InventoryPieceId",
                schema: "inventory",
                table: "receipt_piece_reads",
                column: "InventoryPieceId",
                unique: true,
                filter: "\"InventoryPieceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_piece_reads_ReceiptId_NormalizedSourceSystem_Extern~",
                schema: "inventory",
                table: "receipt_piece_reads",
                columns: new[] { "ReceiptId", "NormalizedSourceSystem", "ExternalIdentifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_piece_reads_ReceiptId_OperationId",
                schema: "inventory",
                table: "receipt_piece_reads",
                columns: new[] { "ReceiptId", "OperationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Receipt barcode reads are commercial traceability records and cannot be dropped automatically.");
        }
    }
}
