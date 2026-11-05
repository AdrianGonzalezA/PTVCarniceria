BEGIN TRANSACTION READ ONLY;
SELECT 'database', current_database(), current_user;
SELECT 'migrations', count(*), max("MigrationId") FROM "__EFMigrationsHistory";
SELECT 'override_table', to_regclass('platform_access.company_role_permission_overrides');
SELECT 'companies', count(*) FROM platform_access.companies;
SELECT 'users', count(*) FROM platform_access.users;
SELECT 'roles', "Code", count(*) FROM platform_access.roles
  WHERE "Code" IN ('administrator', 'cashier') GROUP BY "Code" ORDER BY "Code";
SELECT 'receipt_permission', count(*) FROM platform_access.permissions
  WHERE "Code" = 'inventory.receipts.manage';
SELECT 'writers', count(*) FROM pg_stat_activity
  WHERE datname=current_database() AND pid<>pg_backend_pid() AND state<>'idle';
COMMIT;
