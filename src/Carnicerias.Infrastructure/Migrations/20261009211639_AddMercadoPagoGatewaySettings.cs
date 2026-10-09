using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMercadoPagoGatewaySettings : Migration
    {
        private static readonly string[] TerminalPrincipalColumns = ["CompanyId", "BranchId", "Id"];
        private static readonly string[] TerminalForeignColumns = ["CompanyId", "BranchId", "PosTerminalId"];
        private static readonly string[] PointIdentifierColumns = ["CompanyId", "PointTerminalId"];
        private static readonly string[] QrIdentifierColumns = ["CompanyId", "QrExternalPosId"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mercado_pago_gateway_settings",
                schema: "payments_cash",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerUserId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProtectedAccessToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    ProtectedWebhookSecret = table.Column<byte[]>(type: "bytea", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mercado_pago_gateway_settings", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_mercado_pago_gateway_settings_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mercado_pago_gateway_settings_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mercado_pago_register_settings",
                schema: "payments_cash",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PosTerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    QrExternalPosId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PointTerminalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mercado_pago_register_settings", x => new { x.CompanyId, x.PosTerminalId });
                    table.ForeignKey(
                        name: "FK_mercado_pago_register_settings_pos_terminals_CompanyId_Bran~",
                        columns: x => new { x.CompanyId, x.BranchId, x.PosTerminalId },
                        principalSchema: "platform_access",
                        principalTable: "pos_terminals",
                        principalColumns: TerminalPrincipalColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mercado_pago_register_settings_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_gateway_settings_UpdatedByUserId",
                schema: "payments_cash",
                table: "mercado_pago_gateway_settings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_register_settings_CompanyId_BranchId_PosTermin~",
                schema: "payments_cash",
                table: "mercado_pago_register_settings",
                columns: TerminalForeignColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_register_settings_CompanyId_PointTerminalId",
                schema: "payments_cash",
                table: "mercado_pago_register_settings",
                columns: PointIdentifierColumns,
                unique: true,
                filter: "\"PointTerminalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_register_settings_CompanyId_QrExternalPosId",
                schema: "payments_cash",
                table: "mercado_pago_register_settings",
                columns: QrIdentifierColumns,
                unique: true,
                filter: "\"QrExternalPosId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_register_settings_UpdatedByUserId",
                schema: "payments_cash",
                table: "mercado_pago_register_settings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mercado_pago_gateway_settings",
                schema: "payments_cash");

            migrationBuilder.DropTable(
                name: "mercado_pago_register_settings",
                schema: "payments_cash");
        }
    }
}
