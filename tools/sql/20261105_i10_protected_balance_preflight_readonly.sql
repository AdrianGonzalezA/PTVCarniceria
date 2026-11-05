BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;

SELECT current_database() AS database_name, current_user AS database_user;

WITH live_pieces AS (
    SELECT "CompanyId", "BranchId", "ProductId",
        SUM("ReceivedWeightKg") AS protected_on_hand,
        SUM("ReceivedWeightKg") FILTER (WHERE "Status" = 1) AS protected_reserved,
        SUM("ReceivedWeightKg") FILTER (WHERE "Status" = 5) AS historical_unverified,
        COUNT(*) AS piece_count
    FROM inventory.pieces
    WHERE "Status" IN (0, 1, 5)
    GROUP BY "CompanyId", "BranchId", "ProductId"
), balances AS (
    SELECT b."CompanyId", b."BranchId", b."ProductId", b."OnHand",
        b."Reserved", b."Quarantined",
        COALESCE(p.protected_on_hand, 0) AS protected_on_hand,
        COALESCE(p.protected_reserved, 0) AS protected_reserved,
        COALESCE(p.historical_unverified, 0) AS historical_unverified,
        COALESCE(p.piece_count, 0) AS piece_count
    FROM inventory.branch_inventory b
    LEFT JOIN live_pieces p ON p."CompanyId" = b."CompanyId"
        AND p."BranchId" = b."BranchId" AND p."ProductId" = b."ProductId"
)
SELECT COUNT(*) AS balances,
    COUNT(*) FILTER (WHERE piece_count > 0) AS balances_with_live_pieces,
    COUNT(*) FILTER (WHERE "OnHand" < protected_on_hand) AS below_protected,
    COUNT(*) FILTER (WHERE "Reserved" < protected_reserved) AS reserved_below_protected,
    COUNT(*) FILTER (WHERE protected_on_hand < protected_reserved + "Quarantined")
        AS protected_below_reserved_and_quarantine,
    COUNT(*) FILTER (WHERE "Quarantined" <> historical_unverified)
        AS quarantine_mismatch,
    SUM(protected_on_hand) AS protected_on_hand_total,
    SUM(protected_reserved) AS protected_reserved_total,
    SUM(historical_unverified) AS historical_unverified_total
FROM balances;

SELECT COUNT(*) AS live_pieces_without_balance
FROM inventory.pieces p
LEFT JOIN inventory.branch_inventory b ON b."CompanyId" = p."CompanyId"
    AND b."BranchId" = p."BranchId" AND b."ProductId" = p."ProductId"
WHERE p."Status" IN (0, 1, 5) AND b."ProductId" IS NULL;

ROLLBACK;
