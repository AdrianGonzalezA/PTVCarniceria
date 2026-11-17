-- Development lab only. Keep every existing PLU and assign five-digit PLUs
-- to products without one, independently within each company.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
DO $$
BEGIN
  IF current_database() <> 'carnicerias_test_visual' THEN
    RAISE EXCEPTION 'Wrong database: %', current_database();
  END IF;
END $$;
LOCK TABLE catalog_pricing.products IN SHARE ROW EXCLUSIVE MODE;
WITH missing AS (
  SELECT product."Id", product."CompanyId",
    row_number() OVER (PARTITION BY product."CompanyId" ORDER BY product."Code", product."Id") AS position
  FROM catalog_pricing.products product
  WHERE product."ScalePlu" IS NULL
), available AS (
  SELECT company."CompanyId", lpad(candidate.number::text, 5, '0') AS plu,
    row_number() OVER (PARTITION BY company."CompanyId" ORDER BY candidate.number) AS position
  FROM (SELECT DISTINCT "CompanyId" FROM missing) company
  CROSS JOIN generate_series(10000, 99999) candidate(number)
  WHERE NOT EXISTS (
    SELECT 1 FROM catalog_pricing.products assigned
    WHERE assigned."CompanyId" = company."CompanyId"
      AND assigned."ScalePlu" = lpad(candidate.number::text, 5, '0'))
)
UPDATE catalog_pricing.products product
SET "ScalePlu" = available.plu
FROM missing JOIN available ON available."CompanyId" = missing."CompanyId"
  AND available.position = missing.position
WHERE product."Id" = missing."Id" AND product."CompanyId" = missing."CompanyId";
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM catalog_pricing.products WHERE "ScalePlu" IS NULL) THEN
    RAISE EXCEPTION 'Unassigned PLU remains';
  END IF;
END $$;
COMMIT;
