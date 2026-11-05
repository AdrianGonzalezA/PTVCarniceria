using Carnicerias.Api.ExcelImport;
using Carnicerias.Infrastructure;

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

    private static ImportRow Row(int number, string code, decimal? expected, decimal received) =>
        new("Recepciones", number, new Dictionary<string, ImportCell>
        {
            ["CodigoArticulo"] = new(code, null),
            ["CantidadEsperada"] = new(null, expected),
            ["CantidadRecibida"] = new(null, received)
        });
}
