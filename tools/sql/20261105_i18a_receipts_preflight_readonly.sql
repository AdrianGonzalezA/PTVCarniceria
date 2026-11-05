\set ON_ERROR_STOP on
BEGIN READ ONLY;
SELECT current_database() AS database_name, current_user AS database_user,
       (SELECT count(*) FROM "__EFMigrationsHistory") AS applied_migrations;
SELECT "MigrationId" FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC LIMIT 3;
SELECT count(*) AS suppliers FROM inventory.suppliers;
SELECT count(*) AS movements FROM inventory.inventory_movements;
SELECT count(*) AS balances FROM inventory.branch_inventory;
SELECT count(*) AS existing_receipt_tables FROM information_schema.tables
WHERE table_schema = 'inventory' AND table_name IN ('receipt_documents', 'receipt_lines');
SELECT application_name, state, count(*) AS sessions
FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid()
GROUP BY application_name, state ORDER BY application_name, state;
COMMIT;
