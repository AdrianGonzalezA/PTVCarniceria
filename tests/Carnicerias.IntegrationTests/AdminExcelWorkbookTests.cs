using Carnicerias.Api.ExcelImport;
using ClosedXML.Excel;

namespace Carnicerias.IntegrationTests;

public sealed class AdminExcelWorkbookTests
{
    [Theory]
    [InlineData("categories", "Categorias")]
    [InlineData("products", "Articulos")]
    [InlineData("price-lists", "Listas")]
    [InlineData("customers", "Clientes")]
    public void TemplateKeepsExamplesOutsideLoadSheet(string kind, string loadSheet)
    {
        var bytes = AdminExcelWorkbook.CreateTemplate(kind);
        var parsed = AdminExcelWorkbook.Read(kind, bytes);

        Assert.Empty(parsed.Issues);
        Assert.Empty(parsed.Rows);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.NotNull(workbook.Worksheet(loadSheet));
        Assert.NotNull(workbook.Worksheet("Ejemplos"));
    }

    [Fact]
    public void CodesRemainTextIncludingLeadingZeroes()
    {
        var bytes = AdminExcelWorkbook.CreateTemplate("products");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheet("Articulos").Cell(2, 1).Value = "00123";
        workbook.Worksheet("Articulos").Cell(2, 2).Value = "Asado";
        workbook.Worksheet("Articulos").Cell(2, 3).Value = "Carnes";
        workbook.Worksheet("Articulos").Cell(2, 4).Value = "weight";
        workbook.Worksheet("Articulos").Cell(2, 5).Value = "kg";
        workbook.Worksheet("Articulos").Cell(2, 6).Value = 100.50;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var parsed = AdminExcelWorkbook.Read("products", stream.ToArray());

        Assert.Empty(parsed.Issues);
        Assert.Equal("00123", parsed.Rows.Single(row => row.Sheet == "Articulos").Text("Codigo"));
    }

    [Fact]
    public void FormulaIsRejectedWithoutEvaluation()
    {
        var bytes = AdminExcelWorkbook.CreateTemplate("categories");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheet("Categorias").Cell(2, 1).FormulaA1 = "1+1";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var parsed = AdminExcelWorkbook.Read("categories", stream.ToArray());

        Assert.Contains(parsed.Issues, issue => issue.Code == "FORMULA_NOT_ALLOWED");
    }

    [Fact]
    public void UnknownColumnIsRejectedEvenWhenRequiredColumnsExist()
    {
        var bytes = AdminExcelWorkbook.CreateTemplate("categories");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheet("Categorias").Cell(1, 2).Value = "CampoOculto";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var parsed = AdminExcelWorkbook.Read("categories", stream.ToArray());

        Assert.Contains(parsed.Issues, issue => issue.Code == "UNKNOWN_COLUMN" && issue.Row == 1);
    }

    [Fact]
    public void NumericPrincipalCodeIsRejectedToPreserveLeadingZeroes()
    {
        var bytes = AdminExcelWorkbook.CreateTemplate("products");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheet("Articulos").Cell(2, 1).Value = 123;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var parsed = AdminExcelWorkbook.Read("products", stream.ToArray());

        Assert.Contains(parsed.Issues, issue => issue.Code == "INVALID_CELL_TYPE" &&
            issue.Column == "Codigo" && issue.Row == 2);
    }
}
