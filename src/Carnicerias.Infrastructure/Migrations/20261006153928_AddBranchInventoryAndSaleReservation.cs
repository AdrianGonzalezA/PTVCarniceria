using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchInventoryAndSaleReservation : Migration
    {
        private static readonly string[] CompanyIdAndIdColumns = ["CompanyId", "Id"];
        private static readonly string[] CompanyProductColumns = ["CompanyId", "ProductId"];
        private static readonly string[] ProductMovementColumns = ["CompanyId", "ProductId"];
        private static readonly string[] OperationIdColumns = ["CompanyId", "BranchId", "OperationId"];
        private static readonly string[] MovementHistoryColumns = ["CompanyId", "BranchId", "ProductId", "CreatedAtUtc"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "branch_inventory",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnHand = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Reserved = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_inventory", x => new { x.CompanyId, x.BranchId, x.ProductId });
                    table.CheckConstraint("CK_branch_inventory_on_hand_nonnegative", "\"OnHand\" >= 0");
                    table.CheckConstraint("CK_branch_inventory_reserved_range", "\"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");
                    table.ForeignKey(
                        name: "FK_branch_inventory_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyIdAndIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_branch_inventory_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: CompanyIdAndIdColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_movements", x => x.Id);
                    table.CheckConstraint("CK_inventory_movements_delta_nonzero", "\"QuantityDelta\" <> 0");
                    table.CheckConstraint("CK_inventory_movements_kind", "\"Kind\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_inventory_movements_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyIdAndIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_movements_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: CompanyIdAndIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_movements_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_branch_inventory_CompanyId_ProductId",
                schema: "inventory",
                table: "branch_inventory",
                columns: CompanyProductColumns);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_CompanyId_BranchId_OperationId",
                schema: "inventory",
                table: "inventory_movements",
                columns: OperationIdColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_CompanyId_BranchId_ProductId_CreatedAtU~",
                schema: "inventory",
                table: "inventory_movements",
                columns: MovementHistoryColumns);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_CompanyId_ProductId",
                schema: "inventory",
                table: "inventory_movements",
                columns: ProductMovementColumns);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_UserId",
                schema: "inventory",
                table: "inventory_movements",
                column: "UserId");

            migrationBuilder.Sql("""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES (gen_random_uuid(), 'inventory.stock.manage', 'Administrar existencias de sucursal')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" = 'administrator'
                  AND permission."Code" = 'inventory.stock.manage'
                ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_inventory",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "inventory");

            migrationBuilder.Sql("""
                DELETE FROM platform_access.role_permissions
                WHERE "PermissionId" IN (
                    SELECT "Id" FROM platform_access.permissions WHERE "Code" = 'inventory.stock.manage');
                DELETE FROM platform_access.permissions WHERE "Code" = 'inventory.stock.manage';
                """);
        }
    }
}
