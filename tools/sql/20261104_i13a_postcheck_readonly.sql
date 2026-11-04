\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '60s';

SELECT current_database() AS database_name, current_user AS database_user,
       count(*) AS migration_count, max("MigrationId") AS last_migration
FROM public."__EFMigrationsHistory";

SELECT to_regclass('pos_sales.fiscal_document_snapshots') AS snapshots,
       to_regclass('pos_sales.fiscal_document_vat_lines') AS vat_lines;

SELECT conrelid::regclass AS relation_name, conname, contype
FROM pg_constraint
WHERE conrelid IN ('pos_sales.fiscal_document_snapshots'::regclass,
                   'pos_sales.fiscal_document_vat_lines'::regclass)
ORDER BY relation_name, conname;

SELECT (SELECT count(*) FROM pos_sales.fiscal_document_snapshots) AS snapshots,
       (SELECT count(*) FROM pos_sales.fiscal_document_vat_lines) AS vat_lines,
       (SELECT count(*) FROM pos_sales.fiscal_documents) AS fiscal_attempts,
       (SELECT count(*) FROM pos_sales.confirmed_sales) AS confirmed_sales,
       (SELECT count(*) FROM pos_sales.confirmed_sale_lines) AS confirmed_sale_lines;

COMMIT;
