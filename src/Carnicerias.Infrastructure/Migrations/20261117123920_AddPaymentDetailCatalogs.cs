using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDetailCatalogs : Migration
    {
        private static readonly string[] CardBrandColumns =
            ["Code", "Name", "SupportsDebit", "SupportsCredit", "IsActive"];
        private static readonly string[] NamedCatalogColumns = ["Code", "Name", "IsActive"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payment_config");

            migrationBuilder.CreateTable(
                name: "card_brands",
                schema: "payment_config",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SupportsDebit = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsCredit = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_brands", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "digital_wallets",
                schema: "payment_config",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_digital_wallets", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "financial_institutions",
                schema: "payment_config",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_institutions", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "payment_processors",
                schema: "payment_config",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_processors", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_card_brands_Name",
                schema: "payment_config",
                table: "card_brands",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_digital_wallets_Name",
                schema: "payment_config",
                table: "digital_wallets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_institutions_Name",
                schema: "payment_config",
                table: "financial_institutions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_processors_Name",
                schema: "payment_config",
                table: "payment_processors",
                column: "Name",
                unique: true);

            migrationBuilder.InsertData(
                schema: "payment_config", table: "card_brands",
                columns: CardBrandColumns,
                values: new object[,]
                {
                    { "visa", "Visa", true, true, true },
                    { "mastercard", "Mastercard", true, true, true },
                    { "maestro", "Maestro", true, false, true },
                    { "cabal", "Cabal", true, true, true },
                    { "amex", "American Express", false, true, true },
                    { "diners", "Diners Club", false, true, true },
                    { "naranja", "Naranja", false, true, true },
                    { "tarshop", "Tarjeta Shopping", false, true, true },
                    { "cencosud", "Cencosud", false, true, true },
                    { "argencard", "Argencard", false, true, true },
                    { "cmr", "CMR", false, true, true }
                });

            migrationBuilder.InsertData(
                schema: "payment_config", table: "payment_processors",
                columns: NamedCatalogColumns,
                values: new object[,]
                {
                    { "payway", "Payway", true },
                    { "getnet", "Getnet", true },
                    { "mercado-pago-point", "Mercado Pago Point", true },
                    { "clover", "Clover", true },
                    { "fiserv-posnet", "Fiserv / Posnet", true },
                    { "uala-bis", "Ualá Bis", true }
                });

            migrationBuilder.InsertData(
                schema: "payment_config", table: "financial_institutions",
                columns: NamedCatalogColumns,
                values: new object[,]
                {
                    { "bcra-0011", "Banco Nación", true },
                    { "bcra-0014", "Banco Provincia de Buenos Aires", true },
                    { "bcra-0020", "Banco de la Provincia de Córdoba", true },
                    { "bcra-0007", "Banco Galicia", true },
                    { "bcra-0072", "Banco Santander", true },
                    { "bcra-0017", "Banco BBVA", true },
                    { "bcra-0285", "Banco Macro", true },
                    { "bcra-0191", "Banco Credicoop", true },
                    { "bcra-0029", "Banco Ciudad", true },
                    { "bcra-0034", "Banco Patagonia", true },
                    { "bcra-0027", "Banco Supervielle", true },
                    { "bcra-0044", "Banco Hipotecario", true },
                    { "bcra-0299", "Banco Comafi", true },
                    { "bcra-0015", "ICBC", true },
                    { "bcra-0330", "Banco de Santa Fe", true },
                    { "bcra-0386", "Banco de Entre Ríos", true },
                    { "bcra-0093", "Banco de La Pampa", true },
                    { "bcra-0094", "Banco de Corrientes", true },
                    { "bcra-0311", "Banco del Chaco", true },
                    { "bcra-0315", "Banco de Formosa", true },
                    { "bcra-0321", "Banco de Santiago del Estero", true },
                    { "bcra-0045", "Banco de San Juan", true },
                    { "bcra-0083", "Banco del Chubut", true },
                    { "bcra-0086", "Banco de Santa Cruz", true },
                    { "bcra-0097", "Banco Provincia del Neuquén", true },
                    { "bcra-0268", "Banco de Tierra del Fuego", true },
                    { "bcra-0065", "Banco Municipal de Rosario", true },
                    { "bcra-0143", "Brubank", true }
                });

            migrationBuilder.InsertData(
                schema: "payment_config", table: "digital_wallets",
                columns: NamedCatalogColumns,
                values: new object[,]
                {
                    { "mercado-pago", "Mercado Pago", true },
                    { "uala", "Ualá", true },
                    { "naranja-x", "Naranja X", true },
                    { "personal-pay", "Personal Pay", true },
                    { "cuenta-dni", "Cuenta DNI", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_brands",
                schema: "payment_config");

            migrationBuilder.DropTable(
                name: "digital_wallets",
                schema: "payment_config");

            migrationBuilder.DropTable(
                name: "financial_institutions",
                schema: "payment_config");

            migrationBuilder.DropTable(
                name: "payment_processors",
                schema: "payment_config");
        }
    }
}
