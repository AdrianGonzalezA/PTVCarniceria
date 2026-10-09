using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddArcaCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arca_company_settings",
                schema: "platform_access",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuerCuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    PointOfSale = table.Column<int>(type: "integer", nullable: false),
                    IssuerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuerAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IssuerIibb = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IssuerActivityStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProtectedPfx = table.Column<byte[]>(type: "bytea", nullable: true),
                    ProtectedPassword = table.Column<byte[]>(type: "bytea", nullable: true),
                    CertificateSubject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CertificateThumbprint = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CertificateNotBeforeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CertificateNotAfterUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arca_company_settings", x => x.CompanyId);
                    table.CheckConstraint("CK_arca_company_settings_certificate_range", "\"CertificateNotAfterUtc\" IS NULL OR \"CertificateNotAfterUtc\" > \"CertificateNotBeforeUtc\"");
                    table.CheckConstraint("CK_arca_company_settings_point_of_sale", "\"PointOfSale\" > 0 AND \"PointOfSale\" < 99999");
                    table.ForeignKey(
                        name: "FK_arca_company_settings_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_arca_company_settings_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_arca_company_settings_UpdatedByUserId",
                schema: "platform_access",
                table: "arca_company_settings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "arca_company_settings",
                schema: "platform_access");
        }
    }
}
