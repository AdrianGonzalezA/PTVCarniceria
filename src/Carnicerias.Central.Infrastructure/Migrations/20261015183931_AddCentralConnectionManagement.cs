using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Central.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralConnectionManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastCheckStatus",
                schema: "central_access",
                table: "installations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "notChecked");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCheckedAtUtc",
                schema: "central_access",
                table: "installations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                schema: "central_access",
                table: "installations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "InstallationId",
                schema: "central_access",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_CentralCompanyId_InstallationId_OccurredAtUtc",
                schema: "central_access",
                table: "audit_events",
                columns: ["CentralCompanyId", "InstallationId", "OccurredAtUtc"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_events_CentralCompanyId_InstallationId_OccurredAtUtc",
                schema: "central_access",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "LastCheckStatus",
                schema: "central_access",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "LastCheckedAtUtc",
                schema: "central_access",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "central_access",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "InstallationId",
                schema: "central_access",
                table: "audit_events");
        }
    }
}
