using System;
using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CA1861 // EF migration scaffolding uses constant column arrays.

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScaleTicketOverrideAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES (gen_random_uuid(), 'pos.scale.override',
                    'Autorizar por PIN un peso manual cuando falla la balanza de caja')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" = 'administrator' AND permission."Code" = 'pos.scale.override'
                ON CONFLICT DO NOTHING;
                """);
            migrationBuilder.AddColumn<Guid>(
                name: "ScaleManualOverrideId",
                schema: "pos_sales",
                table: "sale_draft_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeightSource",
                schema: "pos_sales",
                table: "sale_draft_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ScaleManualOverrideId",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeightSource",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "scale_manual_overrides",
                schema: "pos_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PosTerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedSaleDraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedWeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scale_manual_overrides", x => x.Id);
                    table.CheckConstraint("CK_scale_manual_override_weight", "\"AppliedWeightKg\" IS NULL OR \"AppliedWeightKg\" > 0");
                    table.CheckConstraint("CK_scale_manual_override_application", "(\"AppliedSaleDraftId\" IS NULL AND \"AppliedWeightKg\" IS NULL AND \"AppliedAtUtc\" IS NULL) OR (\"AppliedSaleDraftId\" IS NOT NULL AND \"AppliedWeightKg\" IS NOT NULL AND \"AppliedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_scale_manual_overrides_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scale_manual_overrides_cashier_shifts_CashierShiftId",
                        column: x => x.CashierShiftId,
                        principalSchema: "payments_cash",
                        principalTable: "cashier_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scale_manual_overrides_pos_terminals_CompanyId_BranchId_Pos~",
                        columns: x => new { x.CompanyId, x.BranchId, x.PosTerminalId },
                        principalSchema: "platform_access",
                        principalTable: "pos_terminals",
                        principalColumns: new[] { "CompanyId", "BranchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scale_manual_overrides_products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalSchema: "catalog_pricing",
                        principalTable: "products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scale_manual_overrides_sale_drafts_AppliedSaleDraftId",
                        column: x => x.AppliedSaleDraftId,
                        principalSchema: "pos_sales",
                        principalTable: "sale_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scale_manual_overrides_users_AuthorizedByUserId",
                        column: x => x.AuthorizedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scale_manual_overrides_users_CashierId",
                        column: x => x.CashierId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_lines_ScaleManualOverrideId",
                schema: "pos_sales",
                table: "sale_draft_lines",
                column: "ScaleManualOverrideId",
                unique: true,
                filter: "\"ScaleManualOverrideId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sale_lines_ScaleManualOverrideId",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                column: "ScaleManualOverrideId",
                unique: true,
                filter: "\"ScaleManualOverrideId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_scale_manual_overrides_AppliedSaleDraftId",
                schema: "pos_sales",
                table: "scale_manual_overrides",
                column: "AppliedSaleDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_scale_manual_overrides_AuthorizedByUserId",
                schema: "pos_sales",
                table: "scale_manual_overrides",
                column: "AuthorizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_scale_manual_overrides_CashierId",
                schema: "pos_sales",
                table: "scale_manual_overrides",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_scale_manual_overrides_CashierShiftId",
                schema: "pos_sales",
                table: "scale_manual_overrides",
                column: "CashierShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_scale_manual_overrides_CompanyId_BranchId_AuthorizedAtUtc",
                schema: "pos_sales",
                table: "scale_manual_overrides",
                columns: new[] { "CompanyId", "BranchId", "AuthorizedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_scale_manual_overrides_CompanyId_BranchId_PosTerminalId",
                schema: "pos_sales",
                table: "scale_manual_overrides",
                columns: new[] { "CompanyId", "BranchId", "PosTerminalId" });

            migrationBuilder.CreateIndex(
                name: "IX_scale_manual_overrides_CompanyId_ProductId",
                schema: "pos_sales",
                table: "scale_manual_overrides",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_sale_lines_scale_manual_overrides_ScaleManualOver~",
                schema: "pos_sales",
                table: "confirmed_sale_lines",
                column: "ScaleManualOverrideId",
                principalSchema: "pos_sales",
                principalTable: "scale_manual_overrides",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_draft_lines_scale_manual_overrides_ScaleManualOverride~",
                schema: "pos_sales",
                table: "sale_draft_lines",
                column: "ScaleManualOverrideId",
                principalSchema: "pos_sales",
                principalTable: "scale_manual_overrides",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE INDEX "IX_sale_draft_lines_scale_qr_lookup"
                ON pos_sales.sale_draft_lines ("CompanyId", "ScaleLabelReference" varchar_pattern_ops)
                WHERE "ScaleLabelReference" LIKE 'QR:%';
                CREATE INDEX "IX_confirmed_sale_lines_scale_qr_lookup"
                ON pos_sales.confirmed_sale_lines ("CompanyId", "ScaleLabelReference" varchar_pattern_ops)
                WHERE "ScaleLabelReference" LIKE 'QR:%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS pos_sales."IX_sale_draft_lines_scale_qr_lookup";
                DROP INDEX IF EXISTS pos_sales."IX_confirmed_sale_lines_scale_qr_lookup";
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_confirmed_sale_lines_scale_manual_overrides_ScaleManualOver~",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_draft_lines_scale_manual_overrides_ScaleManualOverride~",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropTable(
                name: "scale_manual_overrides",
                schema: "pos_sales");

            migrationBuilder.DropIndex(
                name: "IX_sale_draft_lines_ScaleManualOverrideId",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropIndex(
                name: "IX_confirmed_sale_lines_ScaleManualOverrideId",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "ScaleManualOverrideId",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropColumn(
                name: "WeightSource",
                schema: "pos_sales",
                table: "sale_draft_lines");

            migrationBuilder.DropColumn(
                name: "ScaleManualOverrideId",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.DropColumn(
                name: "WeightSource",
                schema: "pos_sales",
                table: "confirmed_sale_lines");

            migrationBuilder.Sql("""
                DELETE FROM platform_access.company_role_permission_overrides
                WHERE "PermissionId" IN (SELECT "Id" FROM platform_access.permissions
                    WHERE "Code" = 'pos.scale.override');
                DELETE FROM platform_access.role_permissions
                WHERE "PermissionId" IN (SELECT "Id" FROM platform_access.permissions
                    WHERE "Code" = 'pos.scale.override');
                DELETE FROM platform_access.permissions WHERE "Code" = 'pos.scale.override';
                """);
        }
    }
}
