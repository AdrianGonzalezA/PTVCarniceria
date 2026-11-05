BEGIN TRANSACTION READ ONLY;
SELECT 'database', current_database(), current_user;
SELECT 'migrations', count(*), max("MigrationId") FROM "__EFMigrationsHistory";
SELECT 'piece_read_table', to_regclass('inventory.receipt_piece_reads');
SELECT 'read_rows', count(*) FROM inventory.receipt_piece_reads;
SELECT 'receipts', count(*) FROM inventory.receipt_documents;
SELECT 'receipt_lines', count(*) FROM inventory.receipt_lines;
SELECT 'pieces', count(*) FROM inventory.pieces;
SELECT 'fixture_companies', count(*) FROM platform_access.companies
  WHERE "Name" LIKE 'I18a receipt fixture %';
SELECT 'constraints', conname, contype FROM pg_constraint
  WHERE conrelid='inventory.receipt_piece_reads'::regclass ORDER BY conname;
COMMIT;
