BEGIN TRANSACTION READ ONLY;
SELECT 'database', current_database(), current_user;
SELECT 'migrations', count(*), max("MigrationId") FROM "__EFMigrationsHistory";
SELECT 'shift_counts', count(*), count(*) FILTER (WHERE "Status" = 1)
  FROM payments_cash.cashier_shifts;
SELECT 'report_table_exists', to_regclass('payments_cash.shift_closing_reports');
SELECT 'other_connections', count(*) FROM pg_stat_activity
  WHERE datname = current_database() AND pid <> pg_backend_pid()
    AND state <> 'idle';
COMMIT;
