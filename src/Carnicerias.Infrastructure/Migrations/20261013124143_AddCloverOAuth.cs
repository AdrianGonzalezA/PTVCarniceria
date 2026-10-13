using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCloverOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clover_gateway_settings",
                schema: "payments_cash",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProtectedClientSecret = table.Column<byte[]>(type: "bytea", nullable: true),
                    ProtectedAccessToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    ProtectedRefreshToken = table.Column<byte[]>(type: "bytea", nullable: true),
                    AccessTokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clover_gateway_settings", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_clover_gateway_settings_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clover_gateway_settings_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clover_oauth_attempts",
                schema: "payments_cash",
                columns: table => new
                {
                    StateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clover_oauth_attempts", x => x.StateHash);
                    table.ForeignKey(
                        name: "FK_clover_oauth_attempts_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clover_oauth_attempts_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clover_gateway_settings_MerchantId",
                schema: "payments_cash",
                table: "clover_gateway_settings",
                column: "MerchantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clover_gateway_settings_UpdatedByUserId",
                schema: "payments_cash",
                table: "clover_gateway_settings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_clover_oauth_attempts_CompanyId_ExpiresAtUtc",
                schema: "payments_cash",
                table: "clover_oauth_attempts",
                columns: ["CompanyId", "ExpiresAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_clover_oauth_attempts_UserId",
                schema: "payments_cash",
                table: "clover_oauth_attempts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clover_gateway_settings",
                schema: "payments_cash");

            migrationBuilder.DropTable(
                name: "clover_oauth_attempts",
                schema: "payments_cash");
        }
    }
}
