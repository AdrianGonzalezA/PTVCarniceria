using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProtectedStockI10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM inventory.pieces AS p
                        LEFT JOIN inventory.branch_inventory AS b
                          ON b."CompanyId" = p."CompanyId" AND b."BranchId" = p."BranchId"
                         AND b."ProductId" = p."ProductId"
                        WHERE p."Status" IN (0, 1, 5) AND b."ProductId" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'I10: live piece without inventory balance';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM inventory.branch_inventory AS b
                        LEFT JOIN (
                            SELECT "CompanyId", "BranchId", "ProductId",
                                SUM("ReceivedWeightKg") AS historical_weight
                            FROM inventory.pieces WHERE "Status" = 5
                            GROUP BY "CompanyId", "BranchId", "ProductId"
                        ) AS h ON h."CompanyId" = b."CompanyId" AND
                            h."BranchId" = b."BranchId" AND h."ProductId" = b."ProductId"
                        WHERE b."Quarantined" <> COALESCE(h.historical_weight, 0)
                    ) THEN
                        RAISE EXCEPTION 'I10: quarantine differs from historical pieces';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_branch_inventory_on_hand_nonnegative",
                schema: "inventory",
                table: "branch_inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_branch_inventory_reserved_range",
                schema: "inventory",
                table: "branch_inventory");

            migrationBuilder.AddColumn<bool>(
                name: "AllowSaleWithoutStock",
                schema: "catalog_pricing",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProtectedOnHand",
                schema: "inventory",
                table: "branch_inventory",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProtectedReserved",
                schema: "inventory",
                table: "branch_inventory",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE inventory.branch_inventory AS b
                SET "ProtectedOnHand" = p.protected_on_hand,
                    "ProtectedReserved" = p.protected_reserved
                FROM (
                    SELECT "CompanyId", "BranchId", "ProductId",
                        SUM("ReceivedWeightKg") AS protected_on_hand,
                        COALESCE(SUM("ReceivedWeightKg") FILTER (WHERE "Status" = 1), 0)
                            AS protected_reserved
                    FROM inventory.pieces
                    WHERE "Status" IN (0, 1, 5)
                    GROUP BY "CompanyId", "BranchId", "ProductId"
                ) AS p
                WHERE b."CompanyId" = p."CompanyId" AND b."BranchId" = p."BranchId"
                  AND b."ProductId" = p."ProductId";

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM inventory.branch_inventory
                        WHERE "OnHand" < "ProtectedOnHand"
                           OR "Reserved" < "ProtectedReserved"
                           OR "ProtectedOnHand" < "ProtectedReserved" + "Quarantined"
                    ) THEN
                        RAISE EXCEPTION 'I10: protected balance conflicts with historical stock';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_branch_inventory_reserved_range",
                schema: "inventory",
                table: "branch_inventory",
                sql: "\"Reserved\" >= 0 AND \"Quarantined\" >= 0 AND \"ProtectedOnHand\" >= 0 AND \"ProtectedReserved\" >= 0 AND \"Reserved\" >= \"ProtectedReserved\" AND \"ProtectedOnHand\" >= \"ProtectedReserved\" + \"Quarantined\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "I10 protected balances and negative generic stock must be preserved during rollback.");
        }
    }
}
