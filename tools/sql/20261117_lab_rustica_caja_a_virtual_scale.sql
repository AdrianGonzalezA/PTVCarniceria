-- Development lab only. Idempotently assign the local virtual scale to Caja A.
\set ON_ERROR_STOP on
BEGIN;
DO $$
DECLARE
  company_id uuid := '102ee262-53e1-48ed-9756-7c2c84bccb44';
  branch_id uuid := '0d5723bb-ac42-4f69-ab3a-4a23591abdf2';
  terminal_id uuid;
  device_id uuid;
BEGIN
  IF current_database() <> 'carnicerias_test_visual' THEN
    RAISE EXCEPTION 'Wrong database: %', current_database();
  END IF;
  SELECT terminal."Id" INTO STRICT terminal_id
  FROM platform_access.pos_terminals terminal
  JOIN platform_access.branches branch ON branch."Id" = terminal."BranchId"
    AND branch."CompanyId" = terminal."CompanyId"
  JOIN platform_access.companies company ON company."Id" = terminal."CompanyId"
  WHERE terminal."CompanyId" = company_id AND terminal."BranchId" = branch_id
    AND terminal."Name" = 'Caja A' AND branch."Name" = 'Alta Cordoba'
    AND company."Name" = 'Carniceria Rustica SRL' AND terminal."IsActive";

  INSERT INTO platform_access.branch_devices
    ("Id", "CompanyId", "BranchId", "Name", "Type", "Driver", "Port", "BaudRate", "PaperWidthMm", "IsActive")
  SELECT gen_random_uuid(), company_id, branch_id, 'Balanza virtual laboratorio',
    'scale', 'virtual-scale', 'COM99', 9600, NULL, true
  WHERE NOT EXISTS (SELECT 1 FROM platform_access.branch_devices device
    WHERE device."CompanyId" = company_id AND device."BranchId" = branch_id
      AND device."Name" = 'Balanza virtual laboratorio');

  SELECT device."Id" INTO STRICT device_id
  FROM platform_access.branch_devices device
  WHERE device."CompanyId" = company_id AND device."BranchId" = branch_id
    AND device."Name" = 'Balanza virtual laboratorio'
    AND device."Type" = 'scale' AND device."Driver" = 'virtual-scale'
    AND device."Port" = 'COM99' AND device."BaudRate" = 9600 AND device."IsActive";

  UPDATE platform_access.pos_terminals
  SET "ScaleDeviceId" = device_id, "ScalePort" = 'COM99', "ScaleBaudRate" = 9600
  WHERE "Id" = terminal_id AND "CompanyId" = company_id AND "BranchId" = branch_id
    AND "ScaleDeviceId" IS NULL AND "ScalePort" IS NULL;
  IF NOT FOUND AND NOT EXISTS (SELECT 1 FROM platform_access.pos_terminals
      WHERE "Id" = terminal_id AND "ScaleDeviceId" = device_id
        AND "ScalePort" = 'COM99' AND "ScaleBaudRate" = 9600) THEN
    RAISE EXCEPTION 'Caja A already has another scale assignment';
  END IF;
END $$;
COMMIT;
