using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchPriceLists : Migration
    {
        private static readonly string[] CompanyBranchActiveColumns = ["CompanyId", "BranchId", "IsActive"];
        private static readonly string[] CompanyPriceListColumns = ["CompanyId", "PriceListId"];
        private static readonly string[] CompanyIdColumns = ["CompanyId", "Id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog_pricing");

            migrationBuilder.CreateTable(
                name: "price_lists",
                schema: "catalog_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_lists", x => x.Id);
                    table.UniqueConstraint("AK_price_lists_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_price_lists_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "platform_access",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_price_lists",
                schema: "catalog_pricing",
                columns: table => new
                {
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_price_lists", x => new { x.BranchId, x.PriceListId });
                    table.ForeignKey(
                        name: "FK_branch_price_lists_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_branch_price_lists_price_lists_CompanyId_PriceListId",
                        columns: x => new { x.CompanyId, x.PriceListId },
                        principalSchema: "catalog_pricing",
                        principalTable: "price_lists",
                        principalColumns: CompanyIdColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_branch_price_lists_CompanyId_BranchId_IsActive",
                schema: "catalog_pricing",
                table: "branch_price_lists",
                columns: CompanyBranchActiveColumns);

            migrationBuilder.CreateIndex(
                name: "IX_branch_price_lists_CompanyId_PriceListId",
                schema: "catalog_pricing",
                table: "branch_price_lists",
                columns: CompanyPriceListColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_price_lists",
                schema: "catalog_pricing");

            migrationBuilder.DropTable(
                name: "price_lists",
                schema: "catalog_pricing");
        }
    }
}
