using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF-generated migration arrays are immutable in use.

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhysicalCountI21 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.AddColumn<int>(
                name: "CountPolicyOverride",
                schema: "catalog_pricing",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CountPolicy",
                schema: "catalog_pricing",
                table: "categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "physical_count_documents",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_physical_count_documents", x => x.Id);
                    table.UniqueConstraint("AK_physical_count_documents_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_physical_count_documents_decision", "(\"Status\" = 0 AND \"SubmittedByUserId\" IS NULL AND \"DecidedByUserId\" IS NULL) OR (\"Status\" = 1 AND \"SubmittedByUserId\" IS NOT NULL AND \"DecidedByUserId\" IS NULL) OR (\"Status\" IN (2, 3) AND \"SubmittedByUserId\" IS NOT NULL AND \"DecidedByUserId\" IS NOT NULL AND \"DecisionReason\" IS NOT NULL) OR (\"Status\" = 4 AND \"SubmittedByUserId\" IS NULL AND \"DecidedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_physical_count_documents_status", "\"Status\" IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_physical_count_documents_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_physical_count_documents_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_physical_count_documents_users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_physical_count_documents_users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "physical_count_lines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SaleMode = table.Column<int>(type: "integer", nullable: false),
                    Policy = table.Column<int>(type: "integer", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    SystemOnHand = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    SystemReserved = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    SystemProtectedOnHand = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    SystemProtectedReserved = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    SystemQuarantined = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    LastMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InventoryMovementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_physical_count_lines", x => x.Id);
                    table.CheckConstraint("CK_physical_count_lines_counted", "\"CountedQuantity\" BETWEEN 0 AND 100000 AND (\"SaleMode\" <> 1 OR \"CountedQuantity\" = trunc(\"CountedQuantity\"))");
                    table.CheckConstraint("CK_physical_count_lines_policy", "\"Policy\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_physical_count_lines_inventory_movements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalSchema: "inventory",
                        principalTable: "inventory_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_physical_count_lines_physical_count_documents_CompanyId_Cou~",
                        columns: x => new { x.CompanyId, x.CountId },
                        principalSchema: "inventory",
                        principalTable: "physical_count_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_physical_count_lines_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_physical_count_lines_users_CountedByUserId",
                        column: x => x.CountedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_count_policy_override",
                schema: "catalog_pricing",
                table: "products",
                sql: "\"CountPolicyOverride\" IS NULL OR \"CountPolicyOverride\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_categories_count_policy",
                schema: "catalog_pricing",
                table: "categories",
                sql: "\"CountPolicy\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_documents_CompanyId_BranchId_OperationId",
                schema: "inventory",
                table: "physical_count_documents",
                columns: new[] { "CompanyId", "BranchId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_documents_CompanyId_BranchId_Status_CreatedA~",
                schema: "inventory",
                table: "physical_count_documents",
                columns: new[] { "CompanyId", "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_documents_CreatedByUserId",
                schema: "inventory",
                table: "physical_count_documents",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_documents_DecidedByUserId",
                schema: "inventory",
                table: "physical_count_documents",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_documents_SubmittedByUserId",
                schema: "inventory",
                table: "physical_count_documents",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_lines_CompanyId_CountId_ProductId",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "CompanyId", "CountId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_lines_CompanyId_ProductId",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_lines_CountedByUserId",
                schema: "inventory",
                table: "physical_count_lines",
                column: "CountedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_physical_count_lines_InventoryMovementId",
                schema: "inventory",
                table: "physical_count_lines",
                column: "InventoryMovementId",
                unique: true,
                filter: "\"InventoryMovementId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM inventory.physical_count_documents) OR
                       EXISTS (SELECT 1 FROM inventory.inventory_movements WHERE "Kind" = 7) OR
                       EXISTS (SELECT 1 FROM catalog_pricing.categories WHERE "CountPolicy" <> 0) OR
                       EXISTS (SELECT 1 FROM catalog_pricing.products WHERE "CountPolicyOverride" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'I21 has counts or policies; preserve the audit trail';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "physical_count_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "physical_count_documents",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_count_policy_override",
                schema: "catalog_pricing",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_categories_count_policy",
                schema: "catalog_pricing",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "CountPolicyOverride",
                schema: "catalog_pricing",
                table: "products");

            migrationBuilder.DropColumn(
                name: "CountPolicy",
                schema: "catalog_pricing",
                table: "categories");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_movements_kind",
                schema: "inventory",
                table: "inventory_movements",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4, 5, 6)");
        }
    }
}
