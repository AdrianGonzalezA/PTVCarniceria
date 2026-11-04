BEGIN TRANSACTION READ ONLY;
SELECT current_database() AS database_name;
SELECT count(*) AS migrations_applied, max("MigrationId") AS latest_migration
FROM public."__EFMigrationsHistory";
SELECT
  (SELECT count(*) FROM pos_sales.sale_drafts) AS sale_drafts,
  (SELECT count(*) FROM pos_sales.confirmed_sales) AS confirmed_sales,
  (SELECT count(*) FROM payments_cash.point_payment_intents) AS point_payment_intents,
  (SELECT count(*) FROM payments_cash.sale_payments) AS sale_payments,
  (SELECT count(*) FROM platform_access.companies WHERE "Code" LIKE 'I06-%') AS residual_i06_companies;
COMMIT;
