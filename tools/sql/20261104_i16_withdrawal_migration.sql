START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    ALTER TABLE payments_cash.cash_ledger DROP CONSTRAINT "CK_cash_ledger_kind";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    CREATE TABLE pos_sales.cash_withdrawal_requests (
        "Id" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "BranchId" uuid NOT NULL,
        "PosTerminalId" uuid,
        "CashierShiftId" uuid NOT NULL,
        "RequestedByUserId" uuid NOT NULL,
        "OperationId" uuid NOT NULL,
        "Method" integer NOT NULL,
        "Amount" numeric(12,2) NOT NULL,
        "Reason" character varying(500) NOT NULL,
        "RequestedAtUtc" timestamp with time zone NOT NULL,
        "Status" integer NOT NULL,
        "DecidedByUserId" uuid,
        "DecidedAtUtc" timestamp with time zone,
        "DecisionReason" character varying(500),
        "LedgerMovementId" uuid,
        CONSTRAINT "PK_cash_withdrawal_requests" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_cash_withdrawal_cash" CHECK ("Method" = 0 AND "Amount" > 0),
        CONSTRAINT "CK_cash_withdrawal_decision" CHECK (("Status" = 0 AND "DecidedByUserId" IS NULL AND "DecidedAtUtc" IS NULL AND "DecisionReason" IS NULL AND "LedgerMovementId" IS NULL) OR ("Status" = 1 AND "DecidedByUserId" IS NOT NULL AND "DecidedAtUtc" IS NOT NULL AND "DecisionReason" IS NULL AND "LedgerMovementId" IS NOT NULL) OR ("Status" = 2 AND "DecidedByUserId" IS NOT NULL AND "DecidedAtUtc" IS NOT NULL AND "DecisionReason" IS NOT NULL AND "LedgerMovementId" IS NULL)),
        CONSTRAINT "CK_cash_withdrawal_status" CHECK ("Status" IN (0, 1, 2)),
        CONSTRAINT "FK_cash_withdrawal_requests_branches_CompanyId_BranchId" FOREIGN KEY ("CompanyId", "BranchId") REFERENCES platform_access.branches ("CompanyId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_cash_withdrawal_requests_cash_ledger_LedgerMovementId" FOREIGN KEY ("LedgerMovementId") REFERENCES payments_cash.cash_ledger ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_cash_withdrawal_requests_cashier_shifts_CompanyId_CashierSh~" FOREIGN KEY ("CompanyId", "CashierShiftId") REFERENCES payments_cash.cashier_shifts ("CompanyId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_cash_withdrawal_requests_pos_terminals_CompanyId_BranchId_P~" FOREIGN KEY ("CompanyId", "BranchId", "PosTerminalId") REFERENCES platform_access.pos_terminals ("CompanyId", "BranchId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_cash_withdrawal_requests_users_DecidedByUserId" FOREIGN KEY ("DecidedByUserId") REFERENCES platform_access.users ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_cash_withdrawal_requests_users_RequestedByUserId" FOREIGN KEY ("RequestedByUserId") REFERENCES platform_access.users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    ALTER TABLE payments_cash.cash_ledger ADD CONSTRAINT "CK_cash_ledger_kind" CHECK ("Kind" IN (0, 1, 2, 3, 4, 5, 6));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    CREATE UNIQUE INDEX "IX_cash_withdrawal_requests_CompanyId_BranchId_OperationId" ON pos_sales.cash_withdrawal_requests ("CompanyId", "BranchId", "OperationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    CREATE INDEX "IX_cash_withdrawal_requests_CompanyId_BranchId_PosTerminalId" ON pos_sales.cash_withdrawal_requests ("CompanyId", "BranchId", "PosTerminalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    CREATE INDEX "IX_cash_withdrawal_requests_CompanyId_BranchId_Status_Requeste~" ON pos_sales.cash_withdrawal_requests ("CompanyId", "BranchId", "Status", "RequestedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    CREATE INDEX "IX_cash_withdrawal_requests_CompanyId_CashierShiftId_Status" ON pos_sales.cash_withdrawal_requests ("CompanyId", "CashierShiftId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    CREATE INDEX "IX_cash_withdrawal_requests_DecidedByUserId" ON pos_sales.cash_withdrawal_requests ("DecidedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    CREATE UNIQUE INDEX "IX_cash_withdrawal_requests_LedgerMovementId" ON pos_sales.cash_withdrawal_requests ("LedgerMovementId") WHERE "LedgerMovementId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    CREATE INDEX "IX_cash_withdrawal_requests_RequestedByUserId" ON pos_sales.cash_withdrawal_requests ("RequestedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    INSERT INTO platform_access.permissions ("Id", "Code", "Description")
    VALUES ('cd0139e1-a0ad-4377-8e9f-c593ab0f2481',
            'cash.withdrawal.approve',
            'Aprobar o rechazar retiros extraordinarios de efectivo')
    ON CONFLICT ("Code") DO NOTHING;
    INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
    SELECT role."Id", permission."Id"
    FROM platform_access.roles AS role
    CROSS JOIN platform_access.permissions AS permission
    WHERE role."Code" = 'administrator'
      AND permission."Code" = 'cash.withdrawal.approve'
    ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104191635_AddCashWithdrawalApproval') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261104191635_AddCashWithdrawalApproval', '10.0.12');
    END IF;
END $EF$;
COMMIT;
