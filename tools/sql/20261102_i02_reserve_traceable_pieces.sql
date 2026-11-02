-- I02: script generado desde EF Core para revisión. NO EJECUTADO.
-- Requiere autorización específica, respaldo verificado y corte de todos los escritores.
-- Base permitida: carnicerias_test_visual en el contenedor carnicerias-pos-test-20261007.
-- Migración anterior esperada: 20261030151155_AddSalePieceCounts.
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
    IF EXISTS (
        SELECT 1 FROM inventory.pieces AS piece
        WHERE NOT EXISTS (SELECT 1 FROM pos_sales.confirmed_sale_lines AS sold
            WHERE sold."InventoryPieceId" = piece."Id")
          AND NOT EXISTS (SELECT 1 FROM pos_sales.sale_draft_lines AS line
            JOIN pos_sales.sale_drafts AS draft ON draft."Id" = line."SaleDraftId"
            WHERE line."InventoryPieceId" = piece."Id" AND draft."Status" = 0)
          AND (EXISTS (SELECT 1 FROM inventory.inventory_movements AS movement
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
                    sale."ConfirmedAtUtc" >= piece."ReceivedAtUtc"))
    ) THEN
        RAISE EXCEPTION 'I02: unassigned pieces need manual stock reconciliation';
    END IF;
END $$;

ALTER TABLE inventory.pieces ADD "ReservedAtUtc" timestamp with time zone;

ALTER TABLE inventory.pieces ADD "ReservedBySaleDraftId" uuid;

ALTER TABLE inventory.pieces ADD "StateChangedAtUtc" timestamp with time zone;

ALTER TABLE inventory.pieces ADD "Status" integer;

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
    CONSTRAINT "PK_piece_state_events" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_piece_state_events_kind" CHECK ("Kind" BETWEEN 0 AND 7),
    CONSTRAINT "CK_piece_state_events_status" CHECK ("Status" BETWEEN 0 AND 4 AND ("PreviousStatus" IS NULL OR "PreviousStatus" BETWEEN 0 AND 4)),
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

UPDATE inventory.pieces
SET "Status" = 0, "StateChangedAtUtc" = "ReceivedAtUtc"
WHERE "Status" IS NULL;

INSERT INTO inventory.piece_state_events
    ("Id", "CompanyId", "BranchId", "PieceId", "PreviousStatus",
     "Status", "Kind", "SaleDraftId", "ActorUserId", "Reason", "CreatedAtUtc")
SELECT gen_random_uuid(), piece."CompanyId", piece."BranchId", piece."Id",
    NULL, piece."Status", 5, piece."ReservedBySaleDraftId", NULL,
    'Estado inicial verificado al migrar I02', clock_timestamp()
FROM inventory.pieces AS piece;

ALTER TABLE inventory.pieces ALTER COLUMN "Status" SET NOT NULL;

ALTER TABLE inventory.pieces ALTER COLUMN "StateChangedAtUtc" SET NOT NULL;

CREATE INDEX "IX_pieces_CompanyId_BranchId_Status" ON inventory.pieces ("CompanyId", "BranchId", "Status");

CREATE INDEX "IX_pieces_CompanyId_ReservedBySaleDraftId" ON inventory.pieces ("CompanyId", "ReservedBySaleDraftId");

ALTER TABLE inventory.pieces ADD CONSTRAINT "CK_pieces_reservation_owner" CHECK (("Status" = 1 AND "ReservedBySaleDraftId" IS NOT NULL AND "ReservedAtUtc" IS NOT NULL) OR ("Status" <> 1 AND "ReservedBySaleDraftId" IS NULL AND "ReservedAtUtc" IS NULL));

ALTER TABLE inventory.pieces ADD CONSTRAINT "CK_pieces_status" CHECK ("Status" BETWEEN 0 AND 4);

CREATE INDEX "IX_piece_state_events_ActorUserId" ON inventory.piece_state_events ("ActorUserId");

CREATE INDEX "IX_piece_state_events_CompanyId_BranchId_PieceId_CreatedAtUtc" ON inventory.piece_state_events ("CompanyId", "BranchId", "PieceId", "CreatedAtUtc");

CREATE INDEX "IX_piece_state_events_CompanyId_SaleDraftId" ON inventory.piece_state_events ("CompanyId", "SaleDraftId");

CREATE INDEX "IX_piece_state_events_PieceId" ON inventory.piece_state_events ("PieceId");

ALTER TABLE inventory.pieces ADD CONSTRAINT "FK_pieces_sale_drafts_CompanyId_ReservedBySaleDraftId" FOREIGN KEY ("CompanyId", "ReservedBySaleDraftId") REFERENCES pos_sales.sale_drafts ("CompanyId", "Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261102181956_ReserveTraceablePieces', '10.0.12');

COMMIT;
