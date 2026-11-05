BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;

SELECT current_database() AS database_name, current_user AS database_user;
SELECT COUNT(*) AS applied_migrations,
    MAX("MigrationId") AS latest_migration
FROM "__EFMigrationsHistory";

SELECT column_name, data_type, column_default
FROM information_schema.columns
WHERE (table_schema = 'inventory' AND table_name = 'branch_inventory' AND
        column_name IN ('ProtectedOnHand', 'ProtectedReserved')) OR
      (table_schema = 'catalog_pricing' AND table_name = 'products' AND
        column_name = 'AllowSaleWithoutStock')
ORDER BY table_schema, table_name, column_name;

SELECT conname, pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conrelid = 'inventory.branch_inventory'::regclass AND
    conname IN ('CK_branch_inventory_reserved_range',
        'CK_branch_inventory_on_hand_nonnegative');

WITH live_pieces AS (
    SELECT "CompanyId", "BranchId", "ProductId",
        SUM("ReceivedWeightKg") AS protected_on_hand,
        COALESCE(SUM("ReceivedWeightKg") FILTER (WHERE "Status" = 1), 0)
            AS protected_reserved,
        COALESCE(SUM("ReceivedWeightKg") FILTER (WHERE "Status" = 5), 0)
            AS historical_unverified
    FROM inventory.pieces
    WHERE "Status" IN (0, 1, 5)
    GROUP BY "CompanyId", "BranchId", "ProductId"
)
SELECT COUNT(*) AS balances,
    COUNT(*) FILTER (WHERE b."ProtectedOnHand" <> COALESCE(p.protected_on_hand, 0))
        AS protected_on_hand_mismatch,
    COUNT(*) FILTER (WHERE b."ProtectedReserved" <> COALESCE(p.protected_reserved, 0))
        AS protected_reserved_mismatch,
    COUNT(*) FILTER (WHERE b."Quarantined" <> COALESCE(p.historical_unverified, 0))
        AS quarantine_mismatch,
    COUNT(*) FILTER (WHERE b."Reserved" < b."ProtectedReserved" OR
        b."ProtectedOnHand" < b."ProtectedReserved" + b."Quarantined")
        AS invariant_conflicts,
    SUM(b."OnHand") AS total_accounting,
    SUM(b."OnHand" - b."ProtectedOnHand") AS total_generic,
    SUM(b."ProtectedOnHand") AS total_protected
FROM inventory.branch_inventory b
LEFT JOIN live_pieces p ON p."CompanyId" = b."CompanyId" AND
    p."BranchId" = b."BranchId" AND p."ProductId" = b."ProductId";

SELECT COUNT(*) AS products,
    COUNT(*) FILTER (WHERE "AllowSaleWithoutStock") AS enabled_by_default
FROM catalog_pricing.products;

ROLLBACK;
