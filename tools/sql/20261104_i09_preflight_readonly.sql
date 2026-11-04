BEGIN TRANSACTION READ ONLY;
SELECT current_database() AS database_name;
SELECT count(*) AS migrations_applied, max("MigrationId") AS latest_migration
FROM public."__EFMigrationsHistory";
SELECT count(*) AS products,
       count(*) FILTER (WHERE "Cost" IS NULL) AS unknown_costs,
       count(*) FILTER (WHERE "Cost" <= 0 OR "Cost" > 9999999999.99) AS invalid_costs
FROM catalog_pricing.products;
SELECT count(*) AS products_without_open_cost_version
FROM catalog_pricing.products product
WHERE NOT EXISTS (
  SELECT 1 FROM catalog_pricing.product_cost_versions version
  WHERE version."CompanyId" = product."CompanyId"
    AND version."ProductId" = product."Id"
    AND version."EffectiveToUtc" IS NULL
);
SELECT count(*) AS current_cost_mismatches
FROM catalog_pricing.products product
JOIN catalog_pricing.product_cost_versions version
  ON version."CompanyId" = product."CompanyId"
 AND version."ProductId" = product."Id"
 AND version."EffectiveToUtc" IS NULL
WHERE product."Cost" IS DISTINCT FROM version."Amount";
SELECT column_name, is_nullable, data_type, numeric_precision, numeric_scale
FROM information_schema.columns
WHERE table_schema = 'catalog_pricing' AND table_name = 'products' AND column_name = 'Cost';
SELECT conname, pg_get_constraintdef(oid)
FROM pg_constraint
WHERE conrelid = 'catalog_pricing.products'::regclass AND conname = 'CK_products_cost_positive';
COMMIT;
