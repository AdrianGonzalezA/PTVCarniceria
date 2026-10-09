using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotFiscalIssuer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "IssuerActivityStartDate",
                schema: "pos_sales",
                table: "fiscal_documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuerAddress",
                schema: "pos_sales",
                table: "fiscal_documents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuerIibb",
                schema: "pos_sales",
                table: "fiscal_documents",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuerName",
                schema: "pos_sales",
                table: "fiscal_documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssuerActivityStartDate",
                schema: "pos_sales",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "IssuerAddress",
                schema: "pos_sales",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "IssuerIibb",
                schema: "pos_sales",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "IssuerName",
                schema: "pos_sales",
                table: "fiscal_documents");
        }
    }
}
