\set ON_ERROR_STOP on
BEGIN READ ONLY;
SELECT current_database() AS database_name, current_user AS database_user,
       (SELECT count(*) FROM "__EFMigrationsHistory") AS applied_migrations;
SELECT "MigrationId" FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC LIMIT 2;
SELECT count(*) AS receipt_documents FROM inventory.receipt_documents;
SELECT count(*) AS receipt_lines FROM inventory.receipt_lines;
SELECT count(*) AS suppliers FROM inventory.suppliers;
SELECT count(*) AS movements FROM inventory.inventory_movements;
SELECT count(*) AS balances FROM inventory.branch_inventory;
SELECT "Code" FROM platform_access.permissions
WHERE "Code" = 'inventory.receipts.manage';
SELECT conname FROM pg_constraint
WHERE conrelid IN ('inventory.receipt_documents'::regclass,
                   'inventory.receipt_lines'::regclass,
                   'inventory.inventory_movements'::regclass)
  AND conname IN ('CK_receipt_documents_origin', 'CK_receipt_documents_decision',
                  'CK_receipt_lines_quantity', 'CK_inventory_movements_kind')
ORDER BY conname;
COMMIT;
