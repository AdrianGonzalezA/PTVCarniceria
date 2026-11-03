-- I02: comprobación posterior a la migración, sin escritura.
-- Ejecutar antes de reabrir API/cajas y comparar con el prevuelo guardado.
\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '60s';

SELECT current_database() AS database_name,
       (SELECT "MigrationId" FROM "__EFMigrationsHistory"
        ORDER BY "MigrationId" DESC LIMIT 1) AS last_pos_migration,
       (SELECT count(*) FROM "__EFMigrationsHistory") AS migration_count;

SELECT to_regclass('inventory.pieces') AS pieces,
       to_regclass('inventory.piece_state_events') AS piece_state_events;

SELECT attname AS new_piece_column
FROM pg_attribute
WHERE attrelid = 'inventory.pieces'::regclass AND attnum > 0 AND NOT attisdropped
  AND attname IN ('Status', 'ReservedBySaleDraftId', 'ReservedAtUtc', 'StateChangedAtUtc')
ORDER BY attname;

SELECT c.conname, pg_get_constraintdef(c.oid) AS definition
FROM pg_constraint c
WHERE c.conrelid IN ('inventory.pieces'::regclass,
                     'inventory.piece_state_events'::regclass,
                     'inventory.branch_inventory'::regclass)
  AND c.conname IN ('CK_pieces_status', 'CK_pieces_reservation_owner',
                    'FK_pieces_sale_drafts_CompanyId_ReservedBySaleDraftId',
                    'CK_piece_state_events_status', 'CK_piece_state_events_kind',
                    'CK_piece_state_events_reconciliation_evidence',
                    'CK_branch_inventory_reserved_range',
                    'CK_branch_inventory_reconciliation_lock')
ORDER BY c.conname;

SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname = 'inventory' AND tablename IN ('pieces', 'piece_state_events')
  AND indexname IN ('IX_pieces_CompanyId_BranchId_Status',
                    'IX_pieces_CompanyId_ReservedBySaleDraftId',
                    'IX_piece_state_events_CompanyId_BranchId_PieceId_CreatedAtUtc',
                    'IX_piece_state_events_CompanyId_ReconciliationOperationId')
ORDER BY indexname;

SELECT "Status", count(*) AS pieces FROM inventory.pieces
GROUP BY "Status" ORDER BY "Status";

SELECT (SELECT count(*) FROM inventory.pieces) AS total_pieces,
       (SELECT count(*) FROM inventory.piece_state_events WHERE "Kind" = 5)
           AS migration_events,
       (SELECT count(*) FROM inventory.pieces
        WHERE ("Status" = 1 AND ("ReservedBySaleDraftId" IS NULL OR "ReservedAtUtc" IS NULL))
           OR ("Status" <> 1 AND ("ReservedBySaleDraftId" IS NOT NULL OR "ReservedAtUtc" IS NOT NULL)))
           AS invalid_reservation_owners,
       (SELECT count(*) FROM inventory.pieces WHERE "Status" = 1) AS reserved_pieces,
       (SELECT count(*) FROM inventory.pieces WHERE "Status" = 2) AS sold_pieces,
       (SELECT count(*) FROM inventory.pieces WHERE "Status" = 5) AS historical_unverified_pieces,
       (SELECT count(*) FROM inventory.pieces WHERE "Status" = 6) AS reconciled_unavailable_pieces;

SELECT attname AS balance_column
FROM pg_attribute
WHERE attrelid = 'inventory.branch_inventory'::regclass
  AND attnum > 0 AND NOT attisdropped
  AND attname IN ('Quarantined', 'ReconciliationLockId', 'ReconciliationLockPieceId',
                  'ReconciliationLockOwnerUserId', 'ReconciliationLockStartedAtUtc',
                  'ReconciliationLockExpiresAtUtc')
ORDER BY attname;

WITH held AS (
    SELECT "CompanyId", "BranchId", "ProductId", sum("ReceivedWeightKg") AS kg
    FROM inventory.pieces WHERE "Status" = 5
    GROUP BY "CompanyId", "BranchId", "ProductId"
)
SELECT count(*) FILTER (WHERE balance."OnHand" < balance."Reserved" + balance."Quarantined"
                            OR balance."Quarantined" < 0) AS invalid_stock_balances,
       count(*) FILTER (WHERE coalesce(balance."Quarantined", 0) <> coalesce(held.kg, 0))
           AS quarantine_mismatches,
       count(*) FILTER (WHERE balance."ReconciliationLockId" IS NOT NULL)
           AS active_or_expired_reconciliation_leases,
       sum(coalesce(held.kg, 0)) AS historical_quarantine_kg
FROM inventory.branch_inventory balance
FULL JOIN held ON held."CompanyId" = balance."CompanyId" AND
                  held."BranchId" = balance."BranchId" AND
                  held."ProductId" = balance."ProductId";

SELECT count(*) AS pieces_without_exactly_one_backfill
FROM (SELECT piece."Id"
      FROM inventory.pieces piece
      LEFT JOIN inventory.piece_state_events evt
        ON evt."PieceId" = piece."Id" AND evt."Kind" = 5
      GROUP BY piece."Id" HAVING count(evt."Id") <> 1) unexpected;

SELECT (SELECT count(DISTINCT line."InventoryPieceId")
        FROM pos_sales.sale_draft_lines line
        JOIN pos_sales.sale_drafts draft ON draft."Id" = line."SaleDraftId"
        WHERE draft."Status" = 0 AND line."InventoryPieceId" IS NOT NULL)
           AS active_draft_pieces,
       (SELECT count(DISTINCT "InventoryPieceId")
        FROM pos_sales.confirmed_sale_lines WHERE "InventoryPieceId" IS NOT NULL)
           AS confirmed_sold_pieces;

COMMIT;
