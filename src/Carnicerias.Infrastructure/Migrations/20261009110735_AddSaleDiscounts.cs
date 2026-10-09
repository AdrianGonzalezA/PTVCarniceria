using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                schema: "pos_sales",
                table: "sale_drafts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_sale_drafts_discount",
                schema: "pos_sales",
                table: "sale_drafts",
                sql: "\"DiscountAmount\" >= 0 AND (\"DiscountAmount\" = 0 OR \"DiscountReason\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_sales_discount",
                schema: "pos_sales",
                table: "confirmed_sales",
                sql: "\"DiscountAmount\" >= 0 AND (\"DiscountAmount\" = 0 OR \"DiscountReason\" IS NOT NULL)");

            migrationBuilder.Sql("""
                INSERT INTO platform_access.permissions ("Id", "Code", "Description")
                VALUES ('824ba737-0b31-45a2-a230-4a56ebc5ad7f', 'pos.discount.apply',
                    'Aplicar descuentos comerciales al ticket')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO platform_access.role_permissions ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM platform_access.roles AS role
                CROSS JOIN platform_access.permissions AS permission
                WHERE role."Code" IN ('administrator', 'cashier')
                    AND permission."Code" = 'pos.discount.apply'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM platform_access.role_permissions
                WHERE "PermissionId" = '824ba737-0b31-45a2-a230-4a56ebc5ad7f';
                DELETE FROM platform_access.permissions
                WHERE "Id" = '824ba737-0b31-45a2-a230-4a56ebc5ad7f';
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_sale_drafts_discount",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_sales_discount",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "DiscountReason",
                schema: "pos_sales",
                table: "sale_drafts");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "DiscountReason",
                schema: "pos_sales",
                table: "confirmed_sales");
        }
    }
}
