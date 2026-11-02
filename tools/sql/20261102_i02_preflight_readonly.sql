-- I02: ejecutar SOLO contra carnicerias_test_visual. No modifica datos.
-- El operador debe comprobar antes contenedor, puerto e identidad de la base.
\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '60s';

SELECT current_database() AS database_name, current_user AS database_user,
       current_setting('server_version') AS server_version,
       inet_server_port() AS internal_port_if_tcp,
       pg_database_size(current_database()) AS database_bytes;

-- La tabla sin esquema es la misma que usa el script EF de I02.
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory" ORDER BY "MigrationId";

SELECT name, to_regclass(name) AS relation
FROM (VALUES ('inventory.pieces'), ('inventory.inventory_movements'),
             ('inventory.branch_inventory'), ('pos_sales.sale_drafts'),
             ('pos_sales.sale_draft_lines'), ('pos_sales.confirmed_sales'),
             ('pos_sales.confirmed_sale_lines'),
             ('inventory.piece_state_events')) AS expected(name);

SELECT attname AS piece_column
FROM pg_attribute
WHERE attrelid = 'inventory.pieces'::regclass AND attnum > 0 AND NOT attisdropped
ORDER BY attnum;

SELECT schemaname, tablename, indexname, indexdef
FROM pg_indexes
WHERE (schemaname = 'inventory' AND tablename = 'pieces')
   OR (schemaname = 'pos_sales' AND tablename IN
       ('sale_drafts', 'sale_draft_lines', 'confirmed_sale_lines'))
ORDER BY schemaname, tablename, indexname;

SELECT n.nspname AS schema_name, r.relname AS table_name,
       c.conname AS constraint_name, c.contype AS constraint_type,
       pg_get_constraintdef(c.oid) AS definition
FROM pg_constraint c
JOIN pg_class r ON r.oid = c.conrelid
JOIN pg_namespace n ON n.oid = r.relnamespace
WHERE (n.nspname = 'inventory' AND r.relname = 'pieces')
   OR (n.nspname = 'pos_sales' AND r.relname IN
       ('sale_drafts', 'sale_draft_lines', 'confirmed_sale_lines'))
ORDER BY n.nspname, r.relname, c.conname;

SELECT to_regprocedure('pg_catalog.gen_random_uuid()') AS uuid_generator,
       (SELECT count(*) FROM inventory.pieces) AS piece_count,
       (SELECT count(*) FROM pos_sales.sale_drafts WHERE "Status" = 0) AS active_drafts,
       (SELECT count(*) FROM pos_sales.sale_draft_lines WHERE "InventoryPieceId" IS NOT NULL)
           AS draft_piece_lines,
       (SELECT count(*) FROM pos_sales.confirmed_sale_lines WHERE "InventoryPieceId" IS NOT NULL)
           AS sold_piece_lines;

-- Los cinco bloqueos reproducen los predicados de la migración I02.
SELECT 'duplicate_active_claims' AS check_name, count(*) AS conflicts
FROM (SELECT line."InventoryPieceId"
      FROM pos_sales.sale_draft_lines line
      JOIN pos_sales.sale_drafts draft ON draft."Id" = line."SaleDraftId"
      WHERE draft."Status" = 0 AND line."InventoryPieceId" IS NOT NULL
      GROUP BY line."InventoryPieceId" HAVING count(*) > 1) duplicate
UNION ALL
SELECT 'sold_in_active_draft', count(*)
FROM pos_sales.sale_draft_lines line
JOIN pos_sales.sale_drafts draft ON draft."Id" = line."SaleDraftId"
JOIN pos_sales.confirmed_sale_lines sold
  ON sold."InventoryPieceId" = line."InventoryPieceId"
WHERE draft."Status" = 0 AND line."InventoryPieceId" IS NOT NULL
UNION ALL
SELECT 'active_piece_scope_or_weight', count(*)
FROM inventory.pieces piece
JOIN pos_sales.sale_draft_lines line ON line."InventoryPieceId" = piece."Id"
JOIN pos_sales.sale_drafts draft ON draft."Id" = line."SaleDraftId"
WHERE draft."Status" = 0 AND
      (piece."CompanyId" <> draft."CompanyId" OR
       piece."BranchId" <> draft."BranchId" OR
       piece."ProductId" <> line."ProductId" OR
       piece."ReceivedWeightKg" <> line."Quantity")
UNION ALL
SELECT 'sold_piece_scope_or_weight', count(*)
FROM inventory.pieces piece
JOIN pos_sales.confirmed_sale_lines line ON line."InventoryPieceId" = piece."Id"
JOIN pos_sales.confirmed_sales sale ON sale."Id" = line."SaleId"
WHERE piece."CompanyId" <> sale."CompanyId" OR
      piece."BranchId" <> sale."BranchId" OR
      piece."ProductId" <> line."ProductId" OR
      piece."ReceivedWeightKg" <> line."Quantity"
UNION ALL
SELECT 'unassigned_ambiguous_stock', count(*)
FROM inventory.pieces piece
WHERE NOT EXISTS (SELECT 1 FROM pos_sales.confirmed_sale_lines sold
                  WHERE sold."InventoryPieceId" = piece."Id")
  AND NOT EXISTS (SELECT 1 FROM pos_sales.sale_draft_lines line
                  JOIN pos_sales.sale_drafts draft ON draft."Id" = line."SaleDraftId"
                  WHERE line."InventoryPieceId" = piece."Id" AND draft."Status" = 0)
  AND (EXISTS (SELECT 1 FROM inventory.inventory_movements movement
               WHERE movement."CompanyId" = piece."CompanyId" AND
                     movement."BranchId" = piece."BranchId" AND
                     movement."ProductId" = piece."ProductId" AND
                     movement."Kind" = 1 AND movement."QuantityDelta" < 0 AND
                     movement."CreatedAtUtc" >= piece."ReceivedAtUtc")
       OR EXISTS (SELECT 1 FROM pos_sales.confirmed_sale_lines line
                  JOIN pos_sales.confirmed_sales sale ON sale."Id" = line."SaleId"
                  WHERE line."InventoryPieceId" IS NULL AND
                        line."ProductId" = piece."ProductId" AND
                        sale."CompanyId" = piece."CompanyId" AND
                        sale."BranchId" = piece."BranchId" AND
                        sale."ConfirmedAtUtc" >= piece."ReceivedAtUtc"));

-- Control adicional: la reserva agregada debe coincidir con borradores activos.
WITH draft_reserved AS (
    SELECT draft."CompanyId", draft."BranchId", line."ProductId",
           sum(line."Quantity") AS quantity
    FROM pos_sales.sale_drafts draft
    JOIN pos_sales.sale_draft_lines line ON line."SaleDraftId" = draft."Id"
    WHERE draft."Status" = 0
    GROUP BY draft."CompanyId", draft."BranchId", line."ProductId"
)
SELECT count(*) AS inventory_reservation_mismatches
FROM inventory.branch_inventory balance
FULL JOIN draft_reserved draft
  ON draft."CompanyId" = balance."CompanyId" AND
     draft."BranchId" = balance."BranchId" AND
     draft."ProductId" = balance."ProductId"
WHERE coalesce(balance."Reserved", 0) <> coalesce(draft.quantity, 0);

SELECT usename, application_name, state, count(*) AS connections
FROM pg_stat_activity WHERE datname = current_database()
GROUP BY usename, application_name, state ORDER BY usename, application_name, state;

COMMIT;
