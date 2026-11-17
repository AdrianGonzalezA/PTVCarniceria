using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carnicerias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteArgentineBankCatalog2026 : Migration
    {
        private static readonly string[] Columns = ["Code", "Name", "IsActive"];
        private static readonly string[] AddedCodes =
        [
            "bcra-0016", "bcra-0131", "bcra-0147", "bcra-0165", "bcra-0198", "bcra-0247",
            "bcra-0254", "bcra-0266", "bcra-0269", "bcra-0277", "bcra-0281", "bcra-0300",
            "bcra-0301", "bcra-0305", "bcra-0309", "bcra-0310", "bcra-0312", "bcra-0319",
            "bcra-0322", "bcra-0331", "bcra-0332", "bcra-0338", "bcra-0339", "bcra-0340",
            "bcra-0341", "bcra-0384", "bcra-0389", "bcra-0426", "bcra-0431", "bcra-0432",
            "bcra-0435", "bcra-0448"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "payment_config", table: "financial_institutions",
                columns: Columns,
                values: new object[,]
                {
                    { "bcra-0016", "Citibank N.A.", true },
                    { "bcra-0131", "Bank of China", true },
                    { "bcra-0147", "BIBANK", true },
                    { "bcra-0165", "JPMorgan Chase Bank", true },
                    { "bcra-0198", "Banco de Valores", true },
                    { "bcra-0247", "Banco Roela", true },
                    { "bcra-0254", "Banco Mariva", true },
                    { "bcra-0266", "BNP Paribas", true },
                    { "bcra-0269", "Banco de la República Oriental del Uruguay", true },
                    { "bcra-0277", "Banco Sáenz", true },
                    { "bcra-0281", "Banco Meridian", true },
                    { "bcra-0300", "BICE - Banco de Inversión y Comercio Exterior", true },
                    { "bcra-0301", "Banco Piano", true },
                    { "bcra-0305", "Banco Julio", true },
                    { "bcra-0309", "Banco Rioja", true },
                    { "bcra-0310", "Banco del Sol", true },
                    { "bcra-0312", "Banco Voii", true },
                    { "bcra-0319", "Banco CMF", true },
                    { "bcra-0322", "Banco Industrial", true },
                    { "bcra-0331", "Banco Cetelem Argentina", true },
                    { "bcra-0332", "Banco de Servicios Financieros Carrefour", true },
                    { "bcra-0338", "Banco de Servicios y Transacciones", true },
                    { "bcra-0339", "RCI Banque", true },
                    { "bcra-0340", "BACS Banco de Crédito y Securitización", true },
                    { "bcra-0341", "Banco Masventas", true },
                    { "bcra-0384", "Ualá Bank", true },
                    { "bcra-0389", "Banco Columbia", true },
                    { "bcra-0426", "Banco Bica", true },
                    { "bcra-0431", "Banco Coinag", true },
                    { "bcra-0432", "Banco de Comercio", true },
                    { "bcra-0435", "Banco Sucrédito Regional", true },
                    { "bcra-0448", "Banco Dino", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var code in AddedCodes)
                migrationBuilder.DeleteData("financial_institutions", "payment_config", "Code", code);
        }
    }
}
