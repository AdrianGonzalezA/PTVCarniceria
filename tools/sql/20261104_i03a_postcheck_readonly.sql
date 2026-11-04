BEGIN READ ONLY;

SELECT current_database() AS database_name, current_user AS database_user,
       current_setting('server_version') AS postgres_version;

SELECT count(*) AS migrations_applied, max("MigrationId") AS latest_migration
FROM "__EFMigrationsHistory";

SELECT to_regclass('payments_cash.sale_payment_processes') AS processes,
       to_regclass('payments_cash.sale_payment_instruments') AS instruments,
       to_regclass('payments_cash.sale_payment_events') AS events;

SELECT tablename, indexname, indexdef
FROM pg_indexes
WHERE schemaname = 'payments_cash'
  AND tablename IN ('sale_payment_processes', 'sale_payment_instruments',
                    'sale_payment_events', 'point_payment_intents', 'sale_payments')
  AND (indexname LIKE '%CheckoutInstrumentId%'
       OR indexname LIKE 'IX_sale_payment_%'
       OR indexname = 'IX_point_payment_intents_CompanyId_SaleDraftId'
       OR indexname = 'IX_point_payment_intents_ConfirmedSaleId')
ORDER BY tablename, indexname;

SELECT conrelid::regclass AS table_name, conname,
       pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conrelid IN ('payments_cash.sale_payment_processes'::regclass,
                   'payments_cash.sale_payment_instruments'::regclass,
                   'payments_cash.sale_payment_events'::regclass)
ORDER BY conrelid::regclass::text, conname;

SELECT (SELECT count(*) FROM pos_sales.sale_drafts) AS sale_drafts,
       (SELECT count(*) FROM pos_sales.confirmed_sales) AS confirmed_sales,
       (SELECT count(*) FROM payments_cash.point_payment_intents) AS point_payment_intents,
       (SELECT count(*) FROM payments_cash.sale_payments) AS sale_payments,
       (SELECT count(*) FROM payments_cash.sale_payment_processes) AS checkout_processes,
       (SELECT count(*) FROM payments_cash.sale_payment_instruments) AS checkout_instruments,
       (SELECT count(*) FROM payments_cash.sale_payment_events) AS checkout_events;

SELECT (SELECT count(*) FROM payments_cash.point_payment_intents
        WHERE "CheckoutInstrumentId" IS NOT NULL) AS linked_provider_intents,
       (SELECT count(*) FROM payments_cash.sale_payments
        WHERE "CheckoutInstrumentId" IS NOT NULL) AS linked_sale_payments;

SELECT count(*) AS residual_i03a_fixture_companies
FROM platform_access.companies
WHERE "Code" LIKE 'I03A-%';

COMMIT;
