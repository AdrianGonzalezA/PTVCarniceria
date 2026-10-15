using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Central.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCentralAccess : Migration
    {
        private static readonly string[] AdministratorKeyColumns = ["CentralCompanyId", "Id"];
        private static readonly string[] AdministratorUsernameIndexColumns = ["CentralCompanyId", "UsernameNormalized"];
        private static readonly string[] AuditCompanyDateIndexColumns = ["CentralCompanyId", "OccurredAtUtc"];
        private static readonly string[] SessionCompanyAdministratorExpiryIndexColumns = ["CentralCompanyId", "AdministratorId", "ExpiresAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "central_access");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "central_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    CentralCompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "central_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "administrators",
                schema: "central_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CentralCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UsernameNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_administrators", x => x.Id);
                    table.UniqueConstraint("AK_administrators_CentralCompanyId_Id", x => new { x.CentralCompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_administrators_companies_CentralCompanyId",
                        column: x => x.CentralCompanyId,
                        principalSchema: "central_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "central_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CentralCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sessions_administrators_CentralCompanyId_AdministratorId",
                        columns: x => new { x.CentralCompanyId, x.AdministratorId },
                        principalSchema: "central_access",
                        principalTable: "administrators",
                        principalColumns: AdministratorKeyColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_administrators_CentralCompanyId_UsernameNormalized",
                schema: "central_access",
                table: "administrators",
                columns: AdministratorUsernameIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_CentralCompanyId_OccurredAtUtc",
                schema: "central_access",
                table: "audit_events",
                columns: AuditCompanyDateIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_companies_Code",
                schema: "central_access",
                table: "companies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_CentralCompanyId_AdministratorId_ExpiresAtUtc",
                schema: "central_access",
                table: "sessions",
                columns: SessionCompanyAdministratorExpiryIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_TokenHash",
                schema: "central_access",
                table: "sessions",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "central_access");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "central_access");

            migrationBuilder.DropTable(
                name: "administrators",
                schema: "central_access");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "central_access");
        }
    }
}
