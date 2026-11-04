BEGIN TRANSACTION READ ONLY;
SELECT current_database() AS database_name;
SELECT count(*) AS migrations_applied, max("MigrationId") AS latest_migration
FROM public."__EFMigrationsHistory";
SELECT
  (SELECT count(*) FROM platform_access.companies WHERE "Code" LIKE 'I15-%') AS residual_i15_companies,
  (SELECT count(*) FROM payments_cash.cashier_shifts WHERE "CompanyId" IN
    (SELECT "Id" FROM platform_access.companies WHERE "Code" LIKE 'I15-%')) AS residual_i15_shifts,
  (SELECT count(*) FROM pos_sales.confirmed_sales) AS confirmed_sales;
COMMIT;
