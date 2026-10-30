using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchDeviceCatalogAndCustomerFiscalProfile : Migration
    {
        private static readonly string[] CompanyKey = ["CompanyId", "Id"];
        private static readonly string[] BranchDeviceKey = ["CompanyId", "BranchId", "Id"];
        private static readonly string[] PrinterReference = ["CompanyId", "BranchId", "PrinterDeviceId"];
        private static readonly string[] ScaleReference = ["CompanyId", "BranchId", "ScaleDeviceId"];
        private static readonly string[] FiscalCustomerReference = ["CompanyId", "FiscalCustomerId"];
        private static readonly string[] DeviceNameKey = ["CompanyId", "BranchId", "Name"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrinterDeviceId",
                schema: "platform_access",
                table: "pos_terminals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrinterPaperWidthMm",
                schema: "platform_access",
                table: "pos_terminals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScaleDeviceId",
                schema: "platform_access",
                table: "pos_terminals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalAddress",
                schema: "customers_credit",
                table: "customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalDocumentNumber",
                schema: "customers_credit",
                table: "customers",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalLocality",
                schema: "customers_credit",
                table: "customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalProvince",
                schema: "customers_credit",
                table: "customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FiscalTaxStatus",
                schema: "customers_credit",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalCustomerCode",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FiscalCustomerId",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderNumber",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientLocality",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientProvince",
                schema: "pos_sales",
                table: "confirmed_sales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "branch_devices",
                schema: "platform_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Driver = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Port = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    BaudRate = table.Column<int>(type: "integer", nullable: false),
                    PaperWidthMm = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_devices", x => x.Id);
                    table.UniqueConstraint("AK_branch_devices_CompanyId_BranchId_Id", x => new { x.CompanyId, x.BranchId, x.Id });
                    table.ForeignKey(
                        name: "FK_branch_devices_branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "platform_access",
                        principalTable: "branches",
                        principalColumns: CompanyKey,
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve the existing lab assignments: each terminal gets catalog entries
            // with the same serial settings before the new foreign keys are enforced.
            migrationBuilder.Sql("""
                INSERT INTO platform_access.branch_devices
                    ("Id", "CompanyId", "BranchId", "Name", "Type", "Driver",
                     "Port", "BaudRate", "PaperWidthMm", "IsActive")
                SELECT md5(t."Id"::text || ':scale')::uuid, t."CompanyId", t."BranchId",
                       left(t."Name", 90) || ' - balanza - ' || left(t."Id"::text, 8),
                       'scale', 'serial-scale', t."ScalePort", t."ScaleBaudRate", NULL, true
                FROM platform_access.pos_terminals t WHERE t."ScalePort" IS NOT NULL;

                INSERT INTO platform_access.branch_devices
                    ("Id", "CompanyId", "BranchId", "Name", "Type", "Driver",
                     "Port", "BaudRate", "PaperWidthMm", "IsActive")
                SELECT md5(t."Id"::text || ':printer')::uuid, t."CompanyId", t."BranchId",
                       left(t."Name", 90) || ' - impresora - ' || left(t."Id"::text, 8),
                       'printer', t."PrinterKind", t."PrinterPort", t."PrinterBaudRate",
                       NULL, true
                FROM platform_access.pos_terminals t WHERE t."PrinterPort" IS NOT NULL;

                UPDATE platform_access.pos_terminals t
                SET "ScaleDeviceId" = md5(t."Id"::text || ':scale')::uuid
                WHERE t."ScalePort" IS NOT NULL;
                UPDATE platform_access.pos_terminals t
                SET "PrinterDeviceId" = md5(t."Id"::text || ':printer')::uuid
                WHERE t."PrinterPort" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_pos_terminals_CompanyId_BranchId_PrinterDeviceId",
                schema: "platform_access",
                table: "pos_terminals",
                columns: PrinterReference);

            migrationBuilder.CreateIndex(
                name: "IX_pos_terminals_CompanyId_BranchId_ScaleDeviceId",
                schema: "platform_access",
                table: "pos_terminals",
                columns: ScaleReference);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_sales_CompanyId_FiscalCustomerId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: FiscalCustomerReference);

            migrationBuilder.CreateIndex(
                name: "IX_branch_devices_CompanyId_BranchId_Name",
                schema: "platform_access",
                table: "branch_devices",
                columns: DeviceNameKey,
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_confirmed_sales_customers_CompanyId_FiscalCustomerId",
                schema: "pos_sales",
                table: "confirmed_sales",
                columns: FiscalCustomerReference,
                principalSchema: "customers_credit",
                principalTable: "customers",
                principalColumns: CompanyKey,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_terminals_branch_devices_CompanyId_BranchId_PrinterDevi~",
                schema: "platform_access",
                table: "pos_terminals",
                columns: PrinterReference,
                principalSchema: "platform_access",
                principalTable: "branch_devices",
                principalColumns: BranchDeviceKey,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_terminals_branch_devices_CompanyId_BranchId_ScaleDevice~",
                schema: "platform_access",
                table: "pos_terminals",
                columns: ScaleReference,
                principalSchema: "platform_access",
                principalTable: "branch_devices",
                principalColumns: BranchDeviceKey,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_confirmed_sales_customers_CompanyId_FiscalCustomerId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_terminals_branch_devices_CompanyId_BranchId_PrinterDevi~",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_terminals_branch_devices_CompanyId_BranchId_ScaleDevice~",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropTable(
                name: "branch_devices",
                schema: "platform_access");

            migrationBuilder.DropIndex(
                name: "IX_pos_terminals_CompanyId_BranchId_PrinterDeviceId",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropIndex(
                name: "IX_pos_terminals_CompanyId_BranchId_ScaleDeviceId",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropIndex(
                name: "IX_confirmed_sales_CompanyId_FiscalCustomerId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "PrinterDeviceId",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropColumn(
                name: "PrinterPaperWidthMm",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropColumn(
                name: "ScaleDeviceId",
                schema: "platform_access",
                table: "pos_terminals");

            migrationBuilder.DropColumn(
                name: "FiscalAddress",
                schema: "customers_credit",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "FiscalDocumentNumber",
                schema: "customers_credit",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "FiscalLocality",
                schema: "customers_credit",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "FiscalProvince",
                schema: "customers_credit",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "FiscalTaxStatus",
                schema: "customers_credit",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "FiscalCustomerCode",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "FiscalCustomerId",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderNumber",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "RecipientLocality",
                schema: "pos_sales",
                table: "confirmed_sales");

            migrationBuilder.DropColumn(
                name: "RecipientProvince",
                schema: "pos_sales",
                table: "confirmed_sales");
        }
    }
}
