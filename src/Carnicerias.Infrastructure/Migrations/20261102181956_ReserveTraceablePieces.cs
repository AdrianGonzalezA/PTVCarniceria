using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReserveTraceablePieces : Migration
    {
        private static readonly string[] CompanyDraftKey = ["CompanyId", "SaleDraftId"];
        private static readonly string[] CompanyPieceDraftKey = ["CompanyId", "ReservedBySaleDraftId"];
        private static readonly string[] CompanyIdKey = ["CompanyId", "Id"];
        private static readonly string[] CompanyBranchStatus = ["CompanyId", "BranchId", "Status"];
        private static readonly string[] CompanyBranchPieceDate =
            ["CompanyId", "BranchId", "PieceId", "CreatedAtUtc"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // I02 must be applied only during a writer cutover and after a verified backup.
            // These guards deliberately stop rather than guessing the state of historical pieces.
            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReservedAtUtc",
                schema: "inventory",
                table: "pieces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReservedBySaleDraftId",
                schema: "inventory",
                table: "pieces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateChangedAtUtc",
                schema: "inventory",
                table: "pieces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "inventory",
                table: "pieces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quarantined", schema: "inventory", table: "branch_inventory",
                type: "numeric(12,3)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationLockId", schema: "inventory", table: "branch_inventory",
                type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationLockPieceId", schema: "inventory", table: "branch_inventory",
                type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationLockOwnerUserId", schema: "inventory", table: "branch_inventory",
                type: "uuid", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconciliationLockStartedAtUtc", schema: "inventory", table: "branch_inventory",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconciliationLockExpiresAtUtc", schema: "inventory", table: "branch_inventory",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.CreateTable(
                name: "piece_state_events",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PieceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SaleDraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReconciliationOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ObservedWeightKg = table.Column<decimal>(type: "numeric(12,3)", nullable: true),
                    CountedOnHand = table.Column<decimal>(type: "numeric(12,3)", nullable: true),
                    CountedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InventoryMovementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_piece_state_events", x => x.Id);
                    table.CheckConstraint("CK_piece_state_events_kind", "\"Kind\" BETWEEN 0 AND 8");
                    table.CheckConstraint("CK_piece_state_events_status", "\"Status\" BETWEEN 0 AND 6 AND (\"PreviousStatus\" IS NULL OR \"PreviousStatus\" BETWEEN 0 AND 6)");
                    table.CheckConstraint("CK_piece_state_events_reconciliation_evidence", "(\"Kind\" = 8 AND \"PreviousStatus\" IS NOT NULL AND \"PreviousStatus\" = 5 AND \"Status\" IN (0, 6) AND \"ActorUserId\" IS NOT NULL AND \"ReconciliationOperationId\" IS NOT NULL AND \"EvidenceReference\" IS NOT NULL AND \"ObservedWeightKg\" IS NOT NULL AND \"CountedOnHand\" IS NOT NULL AND \"CountedAtUtc\" IS NOT NULL) OR (\"Kind\" <> 8 AND \"ReconciliationOperationId\" IS NULL AND \"EvidenceReference\" IS NULL AND \"EvidenceSha256\" IS NULL AND \"ObservedWeightKg\" IS NULL AND \"CountedOnHand\" IS NULL AND \"CountedAtUtc\" IS NULL AND \"InventoryMovementId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_piece_state_events_inventory_movements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalSchema: "inventory",
                        principalTable: "inventory_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_piece_state_events_pieces_PieceId",
                        column: x => x.PieceId,
                        principalSchema: "inventory",
                        principalTable: "pieces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_piece_state_events_sale_drafts_CompanyId_SaleDraftId",
                        columns: x => new { x.CompanyId, x.SaleDraftId },
                        principalSchema: "pos_sales",
                        principalTable: "sale_drafts",
                        principalColumns: CompanyIdKey,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_piece_state_events_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.AlterColumn<int>(
                name: "Status", schema: "inventory", table: "pieces",
                type: "integer", nullable: false,
                oldClrType: typeof(int), oldType: "integer", oldNullable: true);
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "StateChangedAtUtc", schema: "inventory", table: "pieces",
                type: "timestamp with time zone", nullable: false,
                oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_BranchId_Status",
                schema: "inventory",
                table: "pieces",
                columns: CompanyBranchStatus);

            migrationBuilder.CreateIndex(
                name: "IX_pieces_CompanyId_ReservedBySaleDraftId",
                schema: "inventory",
                table: "pieces",
                columns: CompanyPieceDraftKey);

            migrationBuilder.AddCheckConstraint(
                name: "CK_pieces_reservation_owner",
                schema: "inventory",
                table: "pieces",
                sql: "(\"Status\" = 1 AND \"ReservedBySaleDraftId\" IS NOT NULL AND \"ReservedAtUtc\" IS NOT NULL) OR (\"Status\" <> 1 AND \"ReservedBySaleDraftId\" IS NULL AND \"ReservedAtUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pieces_status",
                schema: "inventory",
                table: "pieces",
                sql: "\"Status\" BETWEEN 0 AND 6");

            migrationBuilder.CreateIndex(
                name: "IX_piece_state_events_ActorUserId",
                schema: "inventory",
                table: "piece_state_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_piece_state_events_CompanyId_BranchId_PieceId_CreatedAtUtc",
                schema: "inventory",
                table: "piece_state_events",
                columns: CompanyBranchPieceDate);

            migrationBuilder.CreateIndex(
                name: "IX_piece_state_events_CompanyId_SaleDraftId",
                schema: "inventory",
                table: "piece_state_events",
                columns: CompanyDraftKey);

            migrationBuilder.CreateIndex(
                name: "IX_piece_state_events_PieceId",
                schema: "inventory",
                table: "piece_state_events",
                column: "PieceId");

            migrationBuilder.CreateIndex(
                name: "IX_piece_state_events_InventoryMovementId",
                schema: "inventory", table: "piece_state_events",
                column: "InventoryMovementId");
            migrationBuilder.CreateIndex(
                name: "IX_piece_state_events_CompanyId_ReconciliationOperationId",
                schema: "inventory", table: "piece_state_events",
                columns: ["CompanyId", "ReconciliationOperationId"],
                unique: true,
                filter: "\"ReconciliationOperationId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_pieces_sale_drafts_CompanyId_ReservedBySaleDraftId",
                schema: "inventory",
                table: "pieces",
                columns: CompanyPieceDraftKey,
                principalSchema: "pos_sales",
                principalTable: "sale_drafts",
                principalColumns: CompanyIdKey,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "I02 state and audit cannot be discarded safely. Stop writers and use a reviewed forward repair or an approved pre-cutover backup.");
        }
    }
}
