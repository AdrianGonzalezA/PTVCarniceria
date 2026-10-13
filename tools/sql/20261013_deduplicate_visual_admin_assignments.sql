-- One-time, guarded correction before ScopeUsersByCompany on carnicerias_test_visual.
-- The original dump is retained outside Git in .local-backups/.
BEGIN;
DO $$
DECLARE
    target_count integer;
    total_count integer;
    deleted_count integer;
BEGIN
    IF current_database() <> 'carnicerias_test_visual' THEN
        RAISE EXCEPTION 'Unexpected database; refusing identity correction';
    END IF;

    SELECT COUNT(*) INTO total_count
    FROM platform_access.user_assignments
    WHERE "UserId" = 'cf394ffc-0003-4c6b-89e3-71e3d5333b0f';
    IF total_count <> 3 THEN
        RAISE EXCEPTION 'visual-admin assignment count changed: %', total_count;
    END IF;

    SELECT COUNT(*) INTO target_count
    FROM platform_access.user_assignments a
    JOIN platform_access.roles r ON r."Id" = a."RoleId"
    WHERE a."UserId" = 'cf394ffc-0003-4c6b-89e3-71e3d5333b0f'
      AND r."Code" = 'administrator'
      AND (a."Id", a."CompanyId") IN (
        ('08d84f7e-0f28-4e2d-8e72-4e7de3ff7aa9', '102ee262-53e1-48ed-9756-7c2c84bccb44'),
        ('23a662f2-536a-4122-acfe-e4bc7a3c0b81', '818b4720-0bf0-48f6-a0a2-599995c10ffd')
      );
    IF target_count <> 2 THEN
        RAISE EXCEPTION 'Expected two exact administrator assignments, found %', target_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM platform_access.user_assignments
        WHERE "Id" = '827b6e51-4f4c-45fc-b393-e21baed5a7f3'
          AND "UserId" = 'cf394ffc-0003-4c6b-89e3-71e3d5333b0f'
          AND "CompanyId" = 'a07b1e5e-9ef3-4e7c-9540-177ebed5084c'
    ) THEN
        RAISE EXCEPTION 'Visual company administrator assignment changed';
    END IF;

    DELETE FROM platform_access.user_assignments
    WHERE "Id" IN (
        '08d84f7e-0f28-4e2d-8e72-4e7de3ff7aa9',
        '23a662f2-536a-4122-acfe-e4bc7a3c0b81'
    );
    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    IF deleted_count <> 2 THEN
        RAISE EXCEPTION 'Expected to remove two assignments, removed %', deleted_count;
    END IF;
END $$;
COMMIT;
