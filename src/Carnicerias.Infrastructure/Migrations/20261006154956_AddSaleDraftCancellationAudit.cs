using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleDraftCancellationAudit : Migration
    {
        private static readonly string[] DraftOwnerColumns = ["CompanyId", "BranchId", "UserId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CancelledByUserId",
                schema: "pos_sales",
                table: "sale_drafts",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: DraftOwnerColumns,
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_sale_drafts_users_CancelledByUserId",
                schema: "pos_sales",
                table: "sale_drafts",
                column: "CancelledByUserId",
                principalSchema: "platform_access",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sale_drafts_users_CancelledByUserId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropIndex(
                name: "IX_sale_drafts_CancelledByUserId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.CreateIndex(
                name: "IX_sale_drafts_CompanyId_BranchId_UserId",
                schema: "pos_sales",
                table: "sale_drafts",
                columns: DraftOwnerColumns,
                unique: true);
        }
    }
}
