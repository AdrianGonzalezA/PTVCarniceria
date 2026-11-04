BEGIN TRANSACTION READ ONLY;
SELECT current_database() AS database_name;
SELECT count(*) AS migrations_applied, max("MigrationId") AS latest_migration
FROM public."__EFMigrationsHistory";
SELECT count(*) AS balances,
       count(*) FILTER (WHERE "OnHand" < 0) AS negative_balances,
       count(*) FILTER (WHERE "Reserved" > 0) AS reserved_balances,
       count(*) FILTER (WHERE "Quarantined" > 0) AS quarantined_balances,
       min("OnHand" - "Reserved" - "Quarantined") AS minimum_available
FROM inventory.branch_inventory;
SELECT count(*) AS pieces,
       count(DISTINCT ("CompanyId", "BranchId", "ProductId")) AS article_branch_groups,
       count(*) FILTER (WHERE "Status" = 5) AS historically_unverified
FROM inventory.pieces;
SELECT count(*) AS groups_with_pieces_and_generic_sales
FROM (
  SELECT DISTINCT piece."CompanyId", piece."BranchId", piece."ProductId"
  FROM inventory.pieces piece
  JOIN pos_sales.confirmed_sales sale
    ON sale."CompanyId" = piece."CompanyId" AND sale."BranchId" = piece."BranchId"
  JOIN pos_sales.confirmed_sale_lines line
    ON line."CompanyId" = sale."CompanyId" AND line."SaleId" = sale."Id"
   AND line."ProductId" = piece."ProductId" AND line."InventoryPieceId" IS NULL
) mixed;
COMMIT;
