using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScopeUsersByCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM platform_access.users AS u
                        LEFT JOIN platform_access.user_assignments AS a ON a."UserId" = u."Id"
                        GROUP BY u."Id"
                        HAVING COUNT(DISTINCT a."CompanyId") <> 1
                    ) THEN
                        RAISE EXCEPTION 'La migración requiere exactamente una empresa por usuario existente; revisar asignaciones antes de continuar.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM platform_access.users
                        WHERE POSITION('@' IN "Username") > 0 OR POSITION('@' IN "Email") = 0
                    ) THEN
                        RAISE EXCEPTION 'La migración detectó credenciales de usuario/correo ambiguas.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "platform_access",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManufacturer",
                schema: "platform_access",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                schema: "platform_access",
                table: "companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE platform_access.companies
                SET "Code" = 'C-' || UPPER(REPLACE("Id"::text, '-', ''));

                UPDATE platform_access.users AS u
                SET "CompanyId" = (
                    SELECT a."CompanyId"
                    FROM platform_access.user_assignments AS a
                    WHERE a."UserId" = u."Id"
                    LIMIT 1
                );

                UPDATE platform_access.sessions
                SET "RevokedAtUtc" = NOW()
                WHERE "RevokedAtUtc" IS NULL AND "ExpiresAtUtc" > NOW();
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "platform_access",
                table: "companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_CompanyId_EmailNormalized",
                schema: "platform_access",
                table: "users",
                columns: ["CompanyId", "EmailNormalized"],
                unique: true,
                filter: "\"IsManufacturer\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_users_CompanyId_UsernameNormalized",
                schema: "platform_access",
                table: "users",
                columns: ["CompanyId", "UsernameNormalized"],
                unique: true,
                filter: "\"IsManufacturer\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_users_manufacturer_email",
                schema: "platform_access",
                table: "users",
                column: "EmailNormalized",
                unique: true,
                filter: "\"IsManufacturer\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_users_manufacturer_username",
                schema: "platform_access",
                table: "users",
                column: "UsernameNormalized",
                unique: true,
                filter: "\"IsManufacturer\" = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_account_scope",
                schema: "platform_access",
                table: "users",
                sql: "(\"IsManufacturer\" = TRUE AND \"CompanyId\" IS NULL) OR (\"IsManufacturer\" = FALSE AND \"CompanyId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_companies_Code",
                schema: "platform_access",
                table: "companies",
                column: "Code",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_users_EmailNormalized",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_UsernameNormalized",
                schema: "platform_access",
                table: "users");

            migrationBuilder.AddForeignKey(
                name: "FK_users_companies_CompanyId",
                schema: "platform_access",
                table: "users",
                column: "CompanyId",
                principalSchema: "platform_access",
                principalTable: "companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    RAISE EXCEPTION 'Reversión bloqueada: restaurar el respaldo verificado de carnicerias_test_visual para no perder códigos ni ámbitos de identidad.';
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_users_companies_CompanyId",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_CompanyId_EmailNormalized",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_CompanyId_UsernameNormalized",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_manufacturer_email",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_manufacturer_username",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_account_scope",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_companies_Code",
                schema: "platform_access",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "IsManufacturer",
                schema: "platform_access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Code",
                schema: "platform_access",
                table: "companies");

            migrationBuilder.CreateIndex(
                name: "IX_users_EmailNormalized",
                schema: "platform_access",
                table: "users",
                column: "EmailNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_UsernameNormalized",
                schema: "platform_access",
                table: "users",
                column: "UsernameNormalized",
                unique: true);
        }
    }
}
