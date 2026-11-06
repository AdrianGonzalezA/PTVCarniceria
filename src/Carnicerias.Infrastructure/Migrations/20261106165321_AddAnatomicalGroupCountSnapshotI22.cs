using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnatomicalGroupCountSnapshotI22 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnatomicalGroup",
                schema: "inventory",
                table: "physical_count_lines",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM inventory.physical_count_lines
                             WHERE "AnatomicalGroup" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot remove anatomical group snapshots from counted lines';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "AnatomicalGroup",
                schema: "inventory",
                table: "physical_count_lines");
        }
    }
}
