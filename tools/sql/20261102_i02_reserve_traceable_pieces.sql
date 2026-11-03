-- I02: SQL generado desde EF Core para revisión. NO EJECUTADO.
-- Requiere autorización separada de respaldo, migración y corte de escritores.
-- Destino exclusivo futuro: carnicerias_test_visual en 127.0.0.1:55433.
START TRANSACTION;
LOCK TABLE inventory.pieces, inventory.inventory_movements,
    pos_sales.sale_drafts, pos_sales.sale_draft_lines,
    pos_sales.confirmed_sales, pos_sales.confirmed_sale_lines
    IN SHARE ROW EXCLUSIVE MODE;
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pos_sales.sale_draft_lines AS line
        JOIN pos_sales.sale_drafts AS draft ON draft."Id" = line."SaleDraftId"
        WHERE draft."Status" = 0 AND line."InventoryPieceId" IS NOT NULL
        GROUP BY line."InventoryPieceId" HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'I02: a piece belongs to multiple active drafts';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pos_sales.sale_draft_lines AS line
        JOIN pos_sales.sale_drafts AS draft ON draft."Id" = line."SaleDraftId"
        JOIN pos_sales.confirmed_sale_lines AS sold
            ON sold."InventoryPieceId" = line."InventoryPieceId"
        WHERE draft."Status" = 0 AND line."InventoryPieceId" IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'I02: a sold piece remains in an active draft';
    END IF;
    IF EXISTS (
        SELECT 1 FROM inventory.pieces AS piece
        JOIN pos_sales.sale_draft_lines AS line ON line."InventoryPieceId" = piece."Id"
        JOIN pos_sales.sale_drafts AS draft ON draft."Id" = line."SaleDraftId"
        WHERE draft."Status" = 0 AND
            (piece."CompanyId" <> draft."CompanyId" OR
             piece."BranchId" <> draft."BranchId" OR
             piece."ProductId" <> line."ProductId" OR
             piece."ReceivedWeightKg" <> line."Quantity")
    ) THEN
        RAISE EXCEPTION 'I02: an active draft piece has mismatched scope or weight';
    END IF;
    IF EXISTS (
        SELECT 1 FROM inventory.pieces AS piece
        JOIN pos_sales.confirmed_sale_lines AS line ON line."InventoryPieceId" = piece."Id"
        JOIN pos_sales.confirmed_sales AS sale ON sale."Id" = line."SaleId"
        WHERE piece."CompanyId" <> sale."CompanyId" OR
            piece."BranchId" <> sale."BranchId" OR
            piece."ProductId" <> line."ProductId" OR
            piece."ReceivedWeightKg" <> line."Quantity"
    ) THEN
        RAISE EXCEPTION 'I02: a sold piece has mismatched scope or weight';
    END IF;
END $$;

ALTER TABLE inventory.pieces ADD "ReservedAtUtc" timestamp with time zone;

ALTER TABLE inventory.pieces ADD "ReservedBySaleDraftId" uuid;

ALTER TABLE inventory.pieces ADD "StateChangedAtUtc" timestamp with time zone;

ALTER TABLE inventory.pieces ADD "Status" integer;

ALTER TABLE inventory.branch_inventory ADD "Quarantined" numeric(12,3) NOT NULL DEFAULT 0.0;

ALTER TABLE inventory.branch_inventory ADD "ReconciliationLockId" uuid;

ALTER TABLE inventory.branch_inventory ADD "ReconciliationLockPieceId" uuid;

ALTER TABLE inventory.branch_inventory ADD "ReconciliationLockOwnerUserId" uuid;

ALTER TABLE inventory.branch_inventory ADD "ReconciliationLockStartedAtUtc" timestamp with time zone;

ALTER TABLE inventory.branch_inventory ADD "ReconciliationLockExpiresAtUtc" timestamp with time zone;

ALTER TABLE inventory.branch_inventory
    DROP CONSTRAINT "CK_branch_inventory_reserved_range";
ALTER TABLE inventory.branch_inventory
    ADD CONSTRAINT "CK_branch_inventory_reserved_range"
    CHECK ("Reserved" >= 0 AND "Quarantined" >= 0 AND
           "Reserved" + "Quarantined" <= "OnHand");
ALTER TABLE inventory.branch_inventory
    ADD CONSTRAINT "CK_branch_inventory_reconciliation_lock"
    CHECK (("ReconciliationLockId" IS NULL AND
            "ReconciliationLockPieceId" IS NULL AND
            "ReconciliationLockOwnerUserId" IS NULL AND
            "ReconciliationLockStartedAtUtc" IS NULL AND
            "ReconciliationLockExpiresAtUtc" IS NULL) OR
           ("ReconciliationLockId" IS NOT NULL AND
            "ReconciliationLockPieceId" IS NOT NULL AND
            "ReconciliationLockOwnerUserId" IS NOT NULL AND
            "ReconciliationLockStartedAtUtc" IS NOT NULL AND
            "ReconciliationLockExpiresAtUtc" IS NOT NULL AND
            "ReconciliationLockExpiresAtUtc" > "ReconciliationLockStartedAtUtc"));

CREATE TABLE inventory.piece_state_events (
    "Id" uuid NOT NULL,
    "CompanyId" uuid NOT NULL,
    "BranchId" uuid NOT NULL,
    "PieceId" uuid NOT NULL,
    "PreviousStatus" integer,
    "Status" integer NOT NULL,
    "Kind" integer NOT NULL,
    "SaleDraftId" uuid,
    "ActorUserId" uuid,
    "Reason" character varying(240) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "ReconciliationOperationId" uuid,
    "EvidenceReference" character varying(512),
    "EvidenceSha256" character varying(64),
    "ObservedWeightKg" numeric(12,3),
    "CountedOnHand" numeric(12,3),
    "CountedAtUtc" timestamp with time zone,
    "InventoryMovementId" uuid,
    CONSTRAINT "PK_piece_state_events" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_piece_state_events_kind" CHECK ("Kind" BETWEEN 0 AND 8),
    CONSTRAINT "CK_piece_state_events_status" CHECK ("Status" BETWEEN 0 AND 6 AND ("PreviousStatus" IS NULL OR "PreviousStatus" BETWEEN 0 AND 6)),
    CONSTRAINT "CK_piece_state_events_reconciliation_evidence" CHECK (("Kind" = 8 AND "PreviousStatus" IS NOT NULL AND "PreviousStatus" = 5 AND "Status" IN (0, 6) AND "ActorUserId" IS NOT NULL AND "ReconciliationOperationId" IS NOT NULL AND "EvidenceReference" IS NOT NULL AND "ObservedWeightKg" IS NOT NULL AND "CountedOnHand" IS NOT NULL AND "CountedAtUtc" IS NOT NULL) OR ("Kind" <> 8 AND "ReconciliationOperationId" IS NULL AND "EvidenceReference" IS NULL AND "EvidenceSha256" IS NULL AND "ObservedWeightKg" IS NULL AND "CountedOnHand" IS NULL AND "CountedAtUtc" IS NULL AND "InventoryMovementId" IS NULL)),
    CONSTRAINT "FK_piece_state_events_inventory_movements_InventoryMovementId" FOREIGN KEY ("InventoryMovementId") REFERENCES inventory.inventory_movements ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_piece_state_events_pieces_PieceId" FOREIGN KEY ("PieceId") REFERENCES inventory.pieces ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_piece_state_events_sale_drafts_CompanyId_SaleDraftId" FOREIGN KEY ("CompanyId", "SaleDraftId") REFERENCES pos_sales.sale_drafts ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_piece_state_events_users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES platform_access.users ("Id") ON DELETE RESTRICT
);

UPDATE inventory.pieces AS piece
SET "Status" = 2, "StateChangedAtUtc" = sale."ConfirmedAtUtc"
FROM pos_sales.confirmed_sale_lines AS line
JOIN pos_sales.confirmed_sales AS sale ON sale."Id" = line."SaleId"
WHERE line."InventoryPieceId" = piece."Id";

UPDATE inventory.pieces AS piece
SET "Status" = 1, "ReservedBySaleDraftId" = draft."Id",
    "ReservedAtUtc" = draft."UpdatedAtUtc",
    "StateChangedAtUtc" = draft."UpdatedAtUtc"
FROM pos_sales.sale_draft_lines AS line
JOIN pos_sales.sale_drafts AS draft ON draft."Id" = line."SaleDraftId"
WHERE line."InventoryPieceId" = piece."Id" AND draft."Status" = 0;

-- The old ambiguity predicate becomes a fail-closed classification, never a sale link.
UPDATE inventory.pieces AS piece
SET "Status" = 5, "StateChangedAtUtc" = clock_timestamp()
WHERE piece."Status" IS NULL AND
    (EXISTS (SELECT 1 FROM inventory.inventory_movements AS movement
        WHERE movement."CompanyId" = piece."CompanyId" AND
              movement."BranchId" = piece."BranchId" AND
              movement."ProductId" = piece."ProductId" AND
              movement."Kind" = 1 AND movement."QuantityDelta" < 0 AND
              movement."CreatedAtUtc" >= piece."ReceivedAtUtc")
     OR EXISTS (SELECT 1 FROM pos_sales.confirmed_sale_lines AS line
        JOIN pos_sales.confirmed_sales AS sale ON sale."Id" = line."SaleId"
        WHERE line."InventoryPieceId" IS NULL AND
              line."ProductId" = piece."ProductId" AND
              sale."CompanyId" = piece."CompanyId" AND
              sale."BranchId" = piece."BranchId" AND
              sale."ConfirmedAtUtc" >= piece."ReceivedAtUtc"));

UPDATE inventory.pieces
SET "Status" = 0, "StateChangedAtUtc" = "ReceivedAtUtc"
WHERE "Status" IS NULL;

UPDATE inventory.branch_inventory AS balance
SET "Quarantined" = held.quantity
FROM (SELECT "CompanyId", "BranchId", "ProductId",
             sum("ReceivedWeightKg") AS quantity
      FROM inventory.pieces WHERE "Status" = 5
      GROUP BY "CompanyId", "BranchId", "ProductId") AS held
WHERE balance."CompanyId" = held."CompanyId" AND
      balance."BranchId" = held."BranchId" AND
      balance."ProductId" = held."ProductId";

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM inventory.pieces AS piece
        WHERE piece."Status" = 5 AND NOT EXISTS
            (SELECT 1 FROM inventory.branch_inventory AS balance
             WHERE balance."CompanyId" = piece."CompanyId" AND
                   balance."BranchId" = piece."BranchId" AND
                   balance."ProductId" = piece."ProductId"))
       OR EXISTS (SELECT 1 FROM inventory.branch_inventory AS balance
           WHERE balance."OnHand" < balance."Reserved" + balance."Quarantined"
              OR balance."Quarantined" < 0
              OR balance."Quarantined" <> COALESCE(
                  (SELECT sum(piece."ReceivedWeightKg")
                   FROM inventory.pieces AS piece
                   WHERE piece."CompanyId" = balance."CompanyId" AND
                         piece."BranchId" = balance."BranchId" AND
                         piece."ProductId" = balance."ProductId" AND
                         piece."Status" = 5), 0))
    THEN RAISE EXCEPTION 'I02: historical quarantine cannot be reconciled with aggregate stock';
    END IF;
END $$;

INSERT INTO inventory.piece_state_events
    ("Id", "CompanyId", "BranchId", "PieceId", "PreviousStatus",
     "Status", "Kind", "SaleDraftId", "ActorUserId", "Reason", "CreatedAtUtc")
SELECT gen_random_uuid(), piece."CompanyId", piece."BranchId", piece."Id",
    NULL, piece."Status", 5, piece."ReservedBySaleDraftId", NULL,
    CASE WHEN piece."Status" = 5
         THEN 'Estado histórico indeterminado; cantidad inmovilizada en I02'
         ELSE 'Estado inicial sin conflicto histórico detectado en I02' END,
    clock_timestamp()
FROM inventory.pieces AS piece;

ALTER TABLE inventory.pieces ALTER COLUMN "Status" SET NOT NULL;

ALTER TABLE inventory.pieces ALTER COLUMN "StateChangedAtUtc" SET NOT NULL;

CREATE INDEX "IX_pieces_CompanyId_BranchId_Status" ON inventory.pieces ("CompanyId", "BranchId", "Status");

CREATE INDEX "IX_pieces_CompanyId_ReservedBySaleDraftId" ON inventory.pieces ("CompanyId", "ReservedBySaleDraftId");

ALTER TABLE inventory.pieces ADD CONSTRAINT "CK_pieces_reservation_owner" CHECK (("Status" = 1 AND "ReservedBySaleDraftId" IS NOT NULL AND "ReservedAtUtc" IS NOT NULL) OR ("Status" <> 1 AND "ReservedBySaleDraftId" IS NULL AND "ReservedAtUtc" IS NULL));

ALTER TABLE inventory.pieces ADD CONSTRAINT "CK_pieces_status" CHECK ("Status" BETWEEN 0 AND 6);

CREATE INDEX "IX_piece_state_events_ActorUserId" ON inventory.piece_state_events ("ActorUserId");

CREATE INDEX "IX_piece_state_events_CompanyId_BranchId_PieceId_CreatedAtUtc" ON inventory.piece_state_events ("CompanyId", "BranchId", "PieceId", "CreatedAtUtc");

CREATE INDEX "IX_piece_state_events_CompanyId_SaleDraftId" ON inventory.piece_state_events ("CompanyId", "SaleDraftId");

CREATE INDEX "IX_piece_state_events_PieceId" ON inventory.piece_state_events ("PieceId");

CREATE INDEX "IX_piece_state_events_InventoryMovementId" ON inventory.piece_state_events ("InventoryMovementId");

CREATE UNIQUE INDEX "IX_piece_state_events_CompanyId_ReconciliationOperationId" ON inventory.piece_state_events ("CompanyId", "ReconciliationOperationId") WHERE "ReconciliationOperationId" IS NOT NULL;

ALTER TABLE inventory.pieces ADD CONSTRAINT "FK_pieces_sale_drafts_CompanyId_ReservedBySaleDraftId" FOREIGN KEY ("CompanyId", "ReservedBySaleDraftId") REFERENCES pos_sales.sale_drafts ("CompanyId", "Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261102181956_ReserveTraceablePieces', '10.0.12');

COMMIT;
