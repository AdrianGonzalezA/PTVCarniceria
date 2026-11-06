using Carnicerias.Api.ExcelImport;
using Carnicerias.Infrastructure;
using ClosedXML.Excel;

namespace Carnicerias.IntegrationTests;

public sealed class ReceiptImportPlannerTests
{
    [Fact]
    public void AlternativeCodeResolvesOneProductWithoutChangingStock()
    {
        var companyId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "2546", "Asado",
            "kg", ProductSaleMode.Weight, null);
        var alternate = new ProductCode(companyId, product.Id, "02546");
        var plan = ReceiptImportPlanner.Plan(companyId, Guid.NewGuid(),
            new ParsedImport([Row(2, "02546", 10m, 8.250m)], []),
            [product], [alternate]);

        Assert.True(plan.Preview.CanApply);
        var line = Assert.Single(plan.Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(10m, line.ExpectedQuantity);
        Assert.Equal(8.250m, line.ReceivedQuantity);
    }

    [Fact]
    public void DuplicateArticleAndFractionalUnitsPreventImport()
    {
        var companyId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "A1", "Chorizo",
            "u.", ProductSaleMode.Unit, null);
        var plan = ReceiptImportPlanner.Plan(companyId, Guid.NewGuid(),
            new ParsedImport([Row(2, "A1", 2m, 1m), Row(3, "A1", 2m, 1.5m)], []),
            [product], []);

        Assert.False(plan.Preview.CanApply);
        Assert.Contains(plan.Preview.Issues, issue => issue.Code == "DUPLICATE_ROW");
        Assert.Contains(plan.Preview.Issues, issue => issue.Code == "INVALID_PRODUCT_QUANTITY");
    }

    [Fact]
    public void PurchasePackageCodeIsNotAnOrdinaryArticleCode()
    {
        var companyId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "A1", "Chorizo",
            "u.", ProductSaleMode.Unit, null);
        var package = new ProductCode(companyId, product.Id, "CAJA6",
            ProductCodeKind.PurchasePackage, 6);
        var plan = ReceiptImportPlanner.Plan(companyId, Guid.NewGuid(),
            new ParsedImport([Row(2, "CAJA6", 1m, 1m)], []), [product], [package]);

        Assert.False(plan.Preview.CanApply);
        Assert.Contains(plan.Preview.Issues, issue => issue.Code == "PRODUCT_NOT_AVAILABLE");
    }

    [Fact]
    public void PackageRowsAndLooseUnitsCreateTwentySixReceivedUnits()
    {
        var companyId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "A1", "Chorizo",
            "u.", ProductSaleMode.Unit, null);
        var six = new ProductCode(companyId, product.Id, "CAJA6",
            ProductCodeKind.PurchasePackage, 6);
        var twelve = new ProductCode(companyId, product.Id, "CAJA12",
            ProductCodeKind.PurchasePackage, 12);
        var plan = ReceiptImportPlanner.Plan(companyId, Guid.NewGuid(), new ParsedImport(
            [Row(2, "A1", null, 2), PackageRow(2, "CAJA6", 2),
                PackageRow(3, "CAJA12", 1)], []), [product], [six, twelve],
            actor, DateTimeOffset.UtcNow);

        Assert.True(plan.Preview.CanApply);
        Assert.Equal(26, plan.Lines.Sum(line => line.ReceivedQuantity));
        Assert.Equal(2, plan.PackageReads.Count);
        Assert.Contains(plan.PackageReads, read => read.Code == "CAJA6" &&
            read.PackageCount == 2 && read.UnitsPerPackage == 6 && read.UnitQuantity == 12);
    }

    [Fact]
    public void PreviousReceiptWorkbookWithoutPackageSheetStillParses()
    {
        using var workbook = new XLWorkbook(new MemoryStream(
            AdminExcelWorkbook.CreateTemplate("receipts")));
        workbook.Worksheet("Bultos").Delete();
        var sheet = workbook.Worksheet("Recepciones");
        sheet.Cell(2, 1).Value = "A1";
        sheet.Cell(2, 3).Value = 2;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var parsed = AdminExcelWorkbook.Read("receipts", stream.ToArray());

        Assert.Empty(parsed.Issues);
        Assert.Single(parsed.Rows);
        Assert.Equal("A1", parsed.Rows[0].Text("CodigoArticulo"));
    }

    private static ImportRow Row(int number, string code, decimal? expected, decimal received) =>
        new("Recepciones", number, new Dictionary<string, ImportCell>
        {
            ["CodigoArticulo"] = new(code, null),
            ["CantidadEsperada"] = new(null, expected),
            ["CantidadRecibida"] = new(null, received)
        });

    private static ImportRow PackageRow(int number, string code, decimal count) =>
        new("Bultos", number, new Dictionary<string, ImportCell>
        {
            ["CodigoBulto"] = new(code, null),
            ["CantidadBultos"] = new(null, count),
            ["CantidadEsperada"] = new(null, null)
        });
}
