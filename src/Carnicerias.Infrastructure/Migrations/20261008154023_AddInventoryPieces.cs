using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryPieces : Migration
    {
        private static readonly string[] ProductColumns = ["CompanyId", "ProductId"];
        private static readonly string[] CompanyEntityColumns = ["CompanyId", "Id"];
        private static readonly string[] OperationColumns = ["CompanyId", "BranchId", "OperationId"];
        private static readonly string[] ProductDateColumns = ["CompanyId", "BranchId", "ProductId", "ReceivedAtUtc"];
        private static readonly string[] ExternalIdentityColumns = ["CompanyId", "NormalizedSourceSystem", "ExternalIdentifier"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.CreateTable(
                name: "pieces",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BarcodeProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryMovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedSourceSystem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExternalIdentifier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RawBarcode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdentifierField = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReceivedWeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pieces", x => x.Id);
                    table.CheckConstraint("CK_pieces_received_weight_positive", "\"ReceivedWeightKg\" > 0");
                    table.ForeignKey(
                        name: "FK_pieces_barcode_profiles_BarcodeProfileId",
                        column: x => x.BarcodeProfileId,
                        principalSchema: "inventory",
                        principalTable: "barcode_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pieces_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pieces_inventory_movements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalSchema: "inventory",
                        principalTable: "inventory_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pieces_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: CompanyEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pieces_users_ReceivedByUserId",
                        column: x => x.ReceivedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_pieces_BarcodeProfileId",
                schema: "inventory",
                table: "pieces",
                column: "BarcodeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_BranchId_OperationId",
                schema: "inventory",
                table: "pieces",
                columns: OperationColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_BranchId_ProductId_ReceivedAtUtc",
                schema: "inventory",
                table: "pieces",
                columns: ProductDateColumns);

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_NormalizedSourceSystem_ExternalIdentifier",
                schema: "inventory",
                table: "pieces",
                columns: ExternalIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_ProductId",
                schema: "inventory",
                table: "pieces",
                columns: ProductColumns);

            migrationBuilder.CreateIndex(
                name: "IX_pieces_InventoryMovementId",
                schema: "inventory",
                table: "pieces",
                column: "InventoryMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_pieces_ReceivedByUserId",
                schema: "inventory",
                table: "pieces",
                column: "ReceivedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pieces",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2)");
        }
    }
}
