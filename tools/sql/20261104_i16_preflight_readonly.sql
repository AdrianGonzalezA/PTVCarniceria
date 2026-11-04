BEGIN TRANSACTION READ ONLY;
SELECT current_database() AS database_name, current_user AS database_user,
       inet_server_port() AS server_port;
SELECT count(*) AS migration_count, max("MigrationId") AS latest_migration
FROM "__EFMigrationsHistory";
SELECT "MigrationId" FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC LIMIT 3;
SELECT to_regclass('payments_cash.cash_ledger') AS cash_ledger,
       to_regclass('payments_cash.cashier_shifts') AS cashier_shifts,
       to_regclass('pos_sales.cash_withdrawal_requests') AS i16_requests_before;
SELECT pg_get_constraintdef(oid) AS ledger_kind_constraint
FROM pg_constraint WHERE conname = 'CK_cash_ledger_kind'
  AND conrelid = 'payments_cash.cash_ledger'::regclass;
SELECT count(*) AS ledger_rows FROM payments_cash.cash_ledger;
SELECT count(*) AS shift_rows FROM payments_cash.cashier_shifts;
SELECT count(*) AS existing_permission
FROM platform_access.permissions WHERE "Code" = 'cash.withdrawal.approve';
SELECT "Code", count(*) AS role_count FROM platform_access.roles
GROUP BY "Code" ORDER BY "Code";
SELECT pid, usename, application_name, state
FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid()
ORDER BY pid;
ROLLBACK;
