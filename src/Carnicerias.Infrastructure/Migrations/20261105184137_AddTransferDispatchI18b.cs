using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Generated EF key and index arrays.

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferDispatchI18b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.CreateTable(
                name: "transfer_documents",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationBranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanceledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanceledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_documents", x => x.Id);
                    table.UniqueConstraint("AK_transfer_documents_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_transfer_documents_decision", "(\"Status\" = 0 AND \"DispatchedByUserId\" IS NULL AND \"DispatchedAtUtc\" IS NULL AND \"CanceledByUserId\" IS NULL AND \"CanceledAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"DispatchedByUserId\" IS NOT NULL AND \"DispatchedAtUtc\" IS NOT NULL AND \"CanceledByUserId\" IS NULL AND \"CanceledAtUtc\" IS NULL) OR (\"Status\" = 2 AND \"DispatchedByUserId\" IS NULL AND \"DispatchedAtUtc\" IS NULL AND \"CanceledByUserId\" IS NOT NULL AND \"CanceledAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_transfer_documents_status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_transfer_documents_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_documents_branches_CompanyId_DestinationBranchId",
                        columns: x => new { x.CompanyId, x.DestinationBranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_documents_users_CanceledByUserId",
                        column: x => x.CanceledByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_documents_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_documents_users_DispatchedByUserId",
                        column: x => x.DispatchedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_lines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SaleMode = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    PieceId = table.Column<Guid>(type: "uuid", nullable: true),
                    InventoryMovementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_lines", x => x.Id);
                    table.CheckConstraint("CK_transfer_lines_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_transfer_lines_inventory_movements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalSchema: "inventory",
                        principalTable: "inventory_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_lines_pieces_PieceId",
                        column: x => x.PieceId,
                        principalSchema: "inventory",
                        principalTable: "pieces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_lines_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_lines_transfer_documents_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "inventory",
                        principalTable: "transfer_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5)");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_documents_CanceledByUserId",
                schema: "inventory",
                table: "transfer_documents",
                column: "CanceledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_documents_CompanyId_BranchId_OperationId",
                schema: "inventory",
                table: "transfer_documents",
                columns: new[] { "CompanyId", "BranchId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfer_documents_CompanyId_BranchId_Status_CreatedAtUtc",
                schema: "inventory",
                table: "transfer_documents",
                columns: new[] { "CompanyId", "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_documents_CompanyId_DestinationBranchId",
                schema: "inventory",
                table: "transfer_documents",
                columns: new[] { "CompanyId", "DestinationBranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_documents_CreatedByUserId",
                schema: "inventory",
                table: "transfer_documents",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_documents_DispatchedByUserId",
                schema: "inventory",
                table: "transfer_documents",
                column: "DispatchedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_lines_CompanyId_ProductId",
                schema: "inventory",
                table: "transfer_lines",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_lines_CompanyId_TransferId_ProductId",
                schema: "inventory",
                table: "transfer_lines",
                columns: new[] { "CompanyId", "TransferId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_lines_InventoryMovementId",
                schema: "inventory",
                table: "transfer_lines",
                column: "InventoryMovementId",
                unique: true,
                filter: "\"InventoryMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_lines_PieceId",
                schema: "inventory",
                table: "transfer_lines",
                column: "PieceId");

            migrationBuilder.Sql("""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES ('30fbd53c-bd98-4f32-8d94-9f0fbfd9bb35',
                        'inventory.transfers.manage',
                        'Preparar y despachar transferencias por remito')
                ON CONFLICT ("Code") DO NOTHING;
                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" = 'administrator'
                  AND permission."Code" = 'inventory.transfers.manage'
                ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Transfer dispatches and movement history cannot be dropped automatically.");
        }
    }
}
