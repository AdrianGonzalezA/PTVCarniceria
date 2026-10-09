using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tax_catalog_entries",
                schema: "catalog_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeactivatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeactivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_catalog_entries", x => x.Id);
                    table.UniqueConstraint("AK_tax_catalog_entries_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_tax_catalog_entries_kind", "\"Kind\" IN (0, 1)");
                    table.CheckConstraint("CK_tax_catalog_entries_rate", "\"RatePercent\" >= 0 AND \"RatePercent\" <= 100");
                    table.ForeignKey(
                        name: "FK_tax_catalog_entries_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tax_catalog_entries_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tax_catalog_entries_users_DeactivatedByUserId",
                        column: x => x.DeactivatedByUserId,
                        principalSchema: "platform_access",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tax_catalog_entries_CompanyId_Code",
                schema: "catalog_pricing",
                table: "tax_catalog_entries",
                columns: ["CompanyId", "Code"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tax_catalog_entries_CreatedByUserId",
                schema: "catalog_pricing",
                table: "tax_catalog_entries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tax_catalog_entries_DeactivatedByUserId",
                schema: "catalog_pricing",
                table: "tax_catalog_entries",
                column: "DeactivatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_catalog_entries",
                schema: "catalog_pricing");
        }
    }
}
