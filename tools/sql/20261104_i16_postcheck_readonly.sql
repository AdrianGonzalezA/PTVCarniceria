BEGIN TRANSACTION READ ONLY;
SELECT current_database() AS database_name, current_user AS database_user,
       inet_server_port() AS server_port;
SELECT count(*) AS migration_count, max("MigrationId") AS latest_migration
FROM "__EFMigrationsHistory";
SELECT to_regclass('pos_sales.cash_withdrawal_requests') AS withdrawal_requests;
SELECT conname, pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conrelid = 'pos_sales.cash_withdrawal_requests'::regclass
ORDER BY conname;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname = 'pos_sales' AND tablename = 'cash_withdrawal_requests'
ORDER BY indexname;
SELECT pg_get_constraintdef(oid) AS ledger_kind_constraint
FROM pg_constraint WHERE conname = 'CK_cash_ledger_kind'
  AND conrelid = 'payments_cash.cash_ledger'::regclass;
SELECT count(*) AS request_rows FROM pos_sales.cash_withdrawal_requests;
SELECT count(*) AS ledger_rows FROM payments_cash.cash_ledger;
SELECT count(*) AS shift_rows FROM payments_cash.cashier_shifts;
SELECT "Id", "Code" FROM platform_access.permissions
WHERE "Code" = 'cash.withdrawal.approve';
SELECT role."Code", count(permission."Id") AS grant_count
FROM platform_access.roles AS role
LEFT JOIN platform_access.role_permissions AS link ON link."RoleId" = role."Id"
LEFT JOIN platform_access.permissions AS permission
  ON permission."Id" = link."PermissionId"
  AND permission."Code" = 'cash.withdrawal.approve'
WHERE role."Code" IN ('administrator', 'cashier')
GROUP BY role."Code" ORDER BY role."Code";
ROLLBACK;
