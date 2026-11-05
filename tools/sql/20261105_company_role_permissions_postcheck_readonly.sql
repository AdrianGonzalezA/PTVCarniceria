BEGIN TRANSACTION READ ONLY;
SELECT 'database', current_database(), current_user;
SELECT 'migrations', count(*), max("MigrationId") FROM "__EFMigrationsHistory";
SELECT 'override_table', to_regclass('platform_access.company_role_permission_overrides');
SELECT 'override_rows', count(*) FROM platform_access.company_role_permission_overrides;
SELECT 'companies', count(*) FROM platform_access.companies;
SELECT 'users', count(*) FROM platform_access.users;
SELECT 'constraints', conname, contype FROM pg_constraint
  WHERE conrelid='platform_access.company_role_permission_overrides'::regclass
  ORDER BY conname;
COMMIT;
