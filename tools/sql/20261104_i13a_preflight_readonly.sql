\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '60s';

SELECT current_database() AS database_name,
       count(*) AS migration_count,
       max("MigrationId") AS last_migration
FROM public."__EFMigrationsHistory";

WITH line_status AS (
    SELECT "SaleId",
           bool_and("NetAfterDiscount" IS NOT NULL) AS amount_present,
           bool_and("TaxRuleId" IS NOT NULL AND "TaxTreatment" IS NOT NULL
                    AND "TaxableBase" IS NOT NULL AND "TaxAmount" IS NOT NULL)
               AS tax_present
    FROM pos_sales.confirmed_sale_lines
    GROUP BY "SaleId"
)
SELECT sale."DocumentType", count(*) AS sales,
       count(*) FILTER (WHERE line_status.amount_present) AS amount_snapshots,
       count(*) FILTER (WHERE line_status.tax_present) AS complete_tax_snapshots,
       count(*) FILTER (WHERE NOT coalesce(line_status.tax_present, false))
           AS incomplete_tax_snapshots
FROM pos_sales.confirmed_sales sale
LEFT JOIN line_status ON line_status."SaleId" = sale."Id"
GROUP BY sale."DocumentType"
ORDER BY sale."DocumentType";

SELECT "Status", count(*) AS fiscal_documents,
       count(*) FILTER (WHERE "Cae" IS NOT NULL) AS with_cae
FROM pos_sales.fiscal_documents
GROUP BY "Status" ORDER BY "Status";

SELECT count(*) AS other_tax_assignments,
       count(*) FILTER (WHERE "EffectiveToUtc" IS NULL) AS currently_active
FROM catalog_pricing.other_tax_assignments;

COMMIT;
