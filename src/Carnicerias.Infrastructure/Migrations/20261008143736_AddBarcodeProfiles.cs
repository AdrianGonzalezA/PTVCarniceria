using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeProfiles : Migration
    {
        private static readonly string[] ProfileRevisionColumns = ["CompanyId", "NormalizedName", "Revision"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "barcode_profiles",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Formula = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    WeightField = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WeightDecimals = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_barcode_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_barcode_profiles_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_barcode_profiles_CompanyId_NormalizedName_Revision",
                schema: "inventory",
                table: "barcode_profiles",
                columns: ProfileRevisionColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "barcode_profiles",
                schema: "inventory");
        }
    }
}
