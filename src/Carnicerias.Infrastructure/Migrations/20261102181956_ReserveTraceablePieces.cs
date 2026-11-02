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
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_piece_state_events", x => x.Id);
                    table.CheckConstraint("CK_piece_state_events_kind", "\"Kind\" BETWEEN 0 AND 7");
                    table.CheckConstraint("CK_piece_state_events_status", "\"Status\" BETWEEN 0 AND 4 AND (\"PreviousStatus\" IS NULL OR \"PreviousStatus\" BETWEEN 0 AND 4)");
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
                sql: "\"Status\" BETWEEN 0 AND 4");

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
