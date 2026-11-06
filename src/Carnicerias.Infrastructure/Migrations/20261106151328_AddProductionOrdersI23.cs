using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF-generated migration arrays are immutable in use.

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionOrdersI23 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_pieces_reservation_owner",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropCheckConstraint(
                name: "CK_piece_state_events_kind",
                schema: "inventory",
                table: "piece_state_events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionOrderId",
                schema: "inventory",
                table: "pieces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReservedByProductionOrderId",
                schema: "inventory",
                table: "pieces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionOrderId",
                schema: "inventory",
                table: "piece_state_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "production_classifiers",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_classifiers", x => x.Id);
                    table.UniqueConstraint("AK_production_classifiers_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_production_classifiers_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_orders",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClassifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectsOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanceledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanceledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReportedWasteKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    WasteReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_orders", x => x.Id);
                    table.UniqueConstraint("AK_production_orders_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_production_orders_decision", "(\"Status\" = 0 AND \"ClosedByUserId\" IS NULL AND \"ClosedAtUtc\" IS NULL AND \"CanceledByUserId\" IS NULL AND \"CanceledAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"ClosedByUserId\" IS NOT NULL AND \"ClosedAtUtc\" IS NOT NULL AND \"CanceledByUserId\" IS NULL AND \"CanceledAtUtc\" IS NULL) OR (\"Status\" = 2 AND \"ClosedByUserId\" IS NULL AND \"ClosedAtUtc\" IS NULL AND \"CanceledByUserId\" IS NOT NULL AND \"CanceledAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_production_orders_kind", "\"Kind\" IN (0, 1)");
                    table.CheckConstraint("CK_production_orders_status", "\"Status\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_production_orders_type", "(\"Kind\" = 0 AND \"ClassifierId\" IS NOT NULL AND \"CorrectsOrderId\" IS NULL) OR (\"Kind\" = 1 AND \"ClassifierId\" IS NULL AND \"CorrectsOrderId\" IS NOT NULL)");
                    table.CheckConstraint("CK_production_orders_waste", "\"ReportedWasteKg\" BETWEEN 0 AND 100000");
                    table.ForeignKey(
                        name: "FK_production_orders_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_orders_production_classifiers_CompanyId_Classifi~",
                        columns: x => new { x.CompanyId, x.ClassifierId },
                        principalSchema: "inventory",
                        principalTable: "production_classifiers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_orders_production_orders_CompanyId_CorrectsOrder~",
                        columns: x => new { x.CompanyId, x.CorrectsOrderId },
                        principalSchema: "inventory",
                        principalTable: "production_orders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_orders_users_CanceledByUserId",
                        column: x => x.CanceledByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_orders_users_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_orders_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_lines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SaleMode = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    MassKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    InputPieceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutputBarcodeProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutputSourceSystem = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OutputExternalIdentifier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    OutputRawBarcode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    OutputIdentifierField = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    InventoryMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutputPieceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_lines", x => x.Id);
                    table.CheckConstraint("CK_production_lines_direction", "\"Direction\" IN (0, 1)");
                    table.CheckConstraint("CK_production_lines_piece", "(\"InputPieceId\" IS NULL OR \"Direction\" = 0) AND ((\"OutputBarcodeProfileId\" IS NULL AND \"OutputSourceSystem\" IS NULL AND \"OutputExternalIdentifier\" IS NULL AND \"OutputRawBarcode\" IS NULL AND \"OutputIdentifierField\" IS NULL) OR (\"OutputBarcodeProfileId\" IS NOT NULL AND \"Direction\" = 1 AND \"OutputSourceSystem\" IS NOT NULL AND \"OutputExternalIdentifier\" IS NOT NULL AND \"OutputRawBarcode\" IS NOT NULL AND \"OutputIdentifierField\" IS NOT NULL))");
                    table.CheckConstraint("CK_production_lines_quantity", "\"Quantity\" > 0 AND \"MassKg\" > 0 AND (\"SaleMode\" <> 1 OR \"Quantity\" = trunc(\"Quantity\")) AND (\"SaleMode\" <> 0 OR \"Quantity\" = \"MassKg\")");
                    table.ForeignKey(
                        name: "FK_production_lines_barcode_profiles_OutputBarcodeProfileId",
                        column: x => x.OutputBarcodeProfileId,
                        principalSchema: "inventory",
                        principalTable: "barcode_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_lines_inventory_movements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalSchema: "inventory",
                        principalTable: "inventory_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_lines_pieces_InputPieceId",
                        column: x => x.InputPieceId,
                        principalSchema: "inventory",
                        principalTable: "pieces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_lines_pieces_OutputPieceId",
                        column: x => x.OutputPieceId,
                        principalSchema: "inventory",
                        principalTable: "pieces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_lines_production_orders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "inventory",
                        principalTable: "production_orders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_lines_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_ProductionOrderId",
                schema: "inventory",
                table: "pieces",
                columns: new[] { "CompanyId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_ReservedByProductionOrderId",
                schema: "inventory",
                table: "pieces",
                columns: new[] { "CompanyId", "ReservedByProductionOrderId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_pieces_reservation_owner",
                schema: "inventory",
                table: "pieces",
                sql: "(\"Status\" = 1 AND (\"ReservedBySaleDraftId\" IS NOT NULL) <> (\"ReservedByProductionOrderId\" IS NOT NULL) AND \"ReservedAtUtc\" IS NOT NULL) OR (\"Status\" <> 1 AND \"ReservedBySaleDraftId\" IS NULL AND \"ReservedByProductionOrderId\" IS NULL AND \"ReservedAtUtc\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_piece_state_events_CompanyId_ProductionOrderId",
                schema: "inventory",
                table: "piece_state_events",
                columns: new[] { "CompanyId", "ProductionOrderId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_piece_state_events_kind",
                schema: "inventory",
                table: "piece_state_events",
                sql: "\"Kind\" BETWEEN 0 AND 12");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9)");

            migrationBuilder.CreateIndex(
                name: "IX_production_classifiers_CompanyId_NormalizedName",
                schema: "inventory",
                table: "production_classifiers",
                columns: new[] { "CompanyId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_CompanyId_OrderId_InputPieceId",
                schema: "inventory",
                table: "production_lines",
                columns: new[] { "CompanyId", "OrderId", "InputPieceId" },
                unique: true,
                filter: "\"InputPieceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_CompanyId_OutputSourceSystem_OutputExterna~",
                schema: "inventory",
                table: "production_lines",
                columns: new[] { "CompanyId", "OutputSourceSystem", "OutputExternalIdentifier" });

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_CompanyId_ProductId",
                schema: "inventory",
                table: "production_lines",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_InputPieceId",
                schema: "inventory",
                table: "production_lines",
                column: "InputPieceId");

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_InventoryMovementId",
                schema: "inventory",
                table: "production_lines",
                column: "InventoryMovementId",
                unique: true,
                filter: "\"InventoryMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_OutputBarcodeProfileId",
                schema: "inventory",
                table: "production_lines",
                column: "OutputBarcodeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_OutputPieceId",
                schema: "inventory",
                table: "production_lines",
                column: "OutputPieceId",
                unique: true,
                filter: "\"OutputPieceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_CanceledByUserId",
                schema: "inventory",
                table: "production_orders",
                column: "CanceledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_ClosedByUserId",
                schema: "inventory",
                table: "production_orders",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_CompanyId_BranchId_OperationId",
                schema: "inventory",
                table: "production_orders",
                columns: new[] { "CompanyId", "BranchId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_CompanyId_BranchId_Status_CreatedAtUtc",
                schema: "inventory",
                table: "production_orders",
                columns: new[] { "CompanyId", "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_CompanyId_ClassifierId",
                schema: "inventory",
                table: "production_orders",
                columns: new[] { "CompanyId", "ClassifierId" });

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_CompanyId_CorrectsOrderId",
                schema: "inventory",
                table: "production_orders",
                columns: new[] { "CompanyId", "CorrectsOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_CreatedByUserId",
                schema: "inventory",
                table: "production_orders",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_piece_state_events_production_orders_CompanyId_ProductionOr~",
                schema: "inventory",
                table: "piece_state_events",
                columns: new[] { "CompanyId", "ProductionOrderId" },
                principalSchema: "inventory",
                principalTable: "production_orders",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pieces_production_orders_CompanyId_ProductionOrderId",
                schema: "inventory",
                table: "pieces",
                columns: new[] { "CompanyId", "ProductionOrderId" },
                principalSchema: "inventory",
                principalTable: "production_orders",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pieces_production_orders_CompanyId_ReservedByProductionOrde~",
                schema: "inventory",
                table: "pieces",
                columns: new[] { "CompanyId", "ReservedByProductionOrderId" },
                principalSchema: "inventory",
                principalTable: "production_orders",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                INSERT INTO platform_access.roles ("Id", "Code", "Name")
                VALUES ('0bd0805f-3f5e-4cea-a59e-948c11928289',
                        'production', 'Producción')
                ON CONFLICT ("Code") DO NOTHING;
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES ('92809199-00d3-47e5-9884-add75877c059',
                        'production.operate', 'Operar órdenes de producción')
                ON CONFLICT ("Code") DO NOTHING;
                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" IN ('administrator', 'production')
                  AND permission."Code" = 'production.operate'
                ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM inventory.production_orders)
                       OR EXISTS (SELECT 1 FROM inventory.production_classifiers)
                       OR EXISTS (SELECT 1 FROM inventory.pieces
                                     WHERE "ProductionOrderId" IS NOT NULL
                                        OR "ReservedByProductionOrderId" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Production data exists; restore a verified backup instead of reversing this migration';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_piece_state_events_production_orders_CompanyId_ProductionOr~",
                schema: "inventory",
                table: "piece_state_events");

            migrationBuilder.DropForeignKey(
                name: "FK_pieces_production_orders_CompanyId_ProductionOrderId",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropForeignKey(
                name: "FK_pieces_production_orders_CompanyId_ReservedByProductionOrde~",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropTable(
                name: "production_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "production_orders",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "production_classifiers",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_pieces_CompanyId_ProductionOrderId",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropIndex(
                name: "IX_pieces_CompanyId_ReservedByProductionOrderId",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pieces_reservation_owner",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropIndex(
                name: "IX_piece_state_events_CompanyId_ProductionOrderId",
                schema: "inventory",
                table: "piece_state_events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_piece_state_events_kind",
                schema: "inventory",
                table: "piece_state_events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "ProductionOrderId",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropColumn(
                name: "ReservedByProductionOrderId",
                schema: "inventory",
                table: "pieces");

            migrationBuilder.DropColumn(
                name: "ProductionOrderId",
                schema: "inventory",
                table: "piece_state_events");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pieces_reservation_owner",
                schema: "inventory",
                table: "pieces",
                sql: "(\"Status\" = 1 AND \"ReservedBySaleDraftId\" IS NOT NULL AND \"ReservedAtUtc\" IS NOT NULL) OR (\"Status\" <> 1 AND \"ReservedBySaleDraftId\" IS NULL AND \"ReservedAtUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_piece_state_events_kind",
                schema: "inventory",
                table: "piece_state_events",
                sql: "\"Kind\" BETWEEN 0 AND 8");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
        }
    }
}
