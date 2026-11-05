BEGIN TRANSACTION READ ONLY;
SELECT 'database', current_database(), current_user;
SELECT 'migrations', count(*), max("MigrationId") FROM "__EFMigrationsHistory";
SELECT 'report_table', to_regclass('payments_cash.shift_closing_reports');
SELECT 'report_columns', column_name, data_type, is_nullable
  FROM information_schema.columns WHERE table_schema='payments_cash' AND table_name='shift_closing_reports'
  ORDER BY ordinal_position;
SELECT 'report_constraints', conname, contype
  FROM pg_constraint WHERE conrelid='payments_cash.shift_closing_reports'::regclass ORDER BY conname;
SELECT 'report_rows', count(*) FROM payments_cash.shift_closing_reports;
SELECT 'fixture_companies', count(*) FROM platform_access.companies
  WHERE "Name" LIKE 'I17 fixture %';
SELECT 'shift_counts', count(*), count(*) FILTER (WHERE "Status" = 1)
  FROM payments_cash.cashier_shifts;
COMMIT;
