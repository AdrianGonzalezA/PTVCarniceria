BEGIN TRANSACTION READ ONLY;
SELECT current_database() AS database_name;
SELECT count(*) AS migrations_applied, max("MigrationId") AS latest_migration
FROM public."__EFMigrationsHistory";
SELECT
  (SELECT count(*) FROM platform_access.companies WHERE "Code" LIKE 'I09-%') AS residual_i09_companies,
  (SELECT count(*) FROM catalog_pricing.products) AS products,
  (SELECT count(*) FROM catalog_pricing.products WHERE "Cost" IS NULL) AS unknown_costs,
  (SELECT count(*) FROM catalog_pricing.product_cost_versions) AS cost_versions,
  (SELECT count(*) FROM pos_sales.confirmed_sales) AS confirmed_sales;
COMMIT;
