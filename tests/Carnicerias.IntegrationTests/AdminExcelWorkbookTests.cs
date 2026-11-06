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
    [InlineData("stock", "Existencias")]
    [InlineData("receipts", "Recepciones")]
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
    public void ProductWorkbookAllowsEmptyCostCellWithoutTreatingItAsZero()
    {
        var bytes = AdminExcelWorkbook.CreateTemplate("products");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Articulos");
        sheet.Cell(2, 1).Value = "00124";
        sheet.Cell(2, 2).Value = "Aguja";
        sheet.Cell(2, 3).Value = "Carnes";
        sheet.Cell(2, 4).Value = "weight";
        sheet.Cell(2, 5).Value = "kg";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var parsed = AdminExcelWorkbook.Read("products", stream.ToArray());

        Assert.Empty(parsed.Issues);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("00124", row.Text("Codigo"));
        Assert.Null(row.NumberValue("CostoARS"));
    }

    [Fact]
    public void ProductWorkbookAcceptsOptionalAnatomicalGroupAndOlderHeaders()
    {
        var bytes = AdminExcelWorkbook.CreateTemplate("products");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Articulos");
        sheet.Cell(2, 1).Value = "LOMO-1";
        sheet.Cell(2, 2).Value = "Lomo limpio";
        sheet.Cell(2, 3).Value = "Carnes";
        sheet.Cell(2, 4).Value = "weight";
        sheet.Cell(2, 5).Value = "kg";
        sheet.Cell(2, 7).Value = "Lomo";
        using var updated = new MemoryStream();
        workbook.SaveAs(updated);
        var parsed = AdminExcelWorkbook.Read("products", updated.ToArray());
        Assert.Empty(parsed.Issues);
        Assert.Equal("Lomo", Assert.Single(parsed.Rows).Text("AgrupadorAnatomico"));

        sheet.Cell(2, 7).Clear();
        sheet.Cell(1, 7).Clear();
        using var oldTemplate = new MemoryStream();
        workbook.SaveAs(oldTemplate);
        var oldParsed = AdminExcelWorkbook.Read("products", oldTemplate.ToArray());
        Assert.Empty(oldParsed.Issues);
        Assert.Null(Assert.Single(oldParsed.Rows).Text("AgrupadorAnatomico"));
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

    [Fact]
    public void StockTemplateKeepsArticleCodeAsTextAndVariationAsNumber()
    {
        var bytes = AdminExcelWorkbook.CreateTemplate("stock");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Existencias");
        Assert.Equal(-1.250, workbook.Worksheet("Ejemplos").Cell(6, 2).GetDouble());
        sheet.Cell(2, 1).Value = "002546";
        sheet.Cell(2, 2).Value = 5.125;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var parsed = AdminExcelWorkbook.Read("stock", stream.ToArray());

        Assert.Empty(parsed.Issues);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("002546", row.Text("CodigoArticulo"));
        Assert.Equal(5.125m, row.NumberValue("Variacion"));
        Assert.Null(row.Text("Descripcion"));
    }

    [Fact]
    public void ReceiptTemplateKeepsArticleCodeAsTextAndQuantitiesAsNumbers()
    {
        var bytes = AdminExcelWorkbook.CreateTemplate("receipts");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Recepciones");
        sheet.Cell(2, 1).Value = "002546";
        sheet.Cell(2, 2).Value = 10.5;
        sheet.Cell(2, 3).Value = 8.25;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var parsed = AdminExcelWorkbook.Read("receipts", stream.ToArray());

        Assert.Empty(parsed.Issues);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("002546", row.Text("CodigoArticulo"));
        Assert.Equal(10.5m, row.NumberValue("CantidadEsperada"));
        Assert.Equal(8.25m, row.NumberValue("CantidadRecibida"));
    }
}
