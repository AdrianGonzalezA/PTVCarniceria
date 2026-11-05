BEGIN TRANSACTION READ ONLY;
SELECT 'database', current_database(), current_user;
SELECT 'migrations', count(*), max("MigrationId") FROM "__EFMigrationsHistory";
SELECT 'piece_read_table', to_regclass('inventory.receipt_piece_reads');
SELECT 'receipts', count(*) FROM inventory.receipt_documents;
SELECT 'receipt_lines', count(*) FROM inventory.receipt_lines;
SELECT 'pieces', count(*) FROM inventory.pieces;
SELECT 'profile_identity_conflicts', count(*) FROM (
  SELECT "CompanyId", "Id" FROM inventory.barcode_profiles
  GROUP BY "CompanyId", "Id" HAVING count(*) > 1
) AS duplicates;
SELECT 'other_connections', count(*) FROM pg_stat_activity
  WHERE datname=current_database() AND pid<>pg_backend_pid() AND state<>'idle';
COMMIT;
