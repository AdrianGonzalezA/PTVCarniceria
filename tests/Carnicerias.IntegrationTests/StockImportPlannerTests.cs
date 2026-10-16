using Carnicerias.Api.ExcelImport;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class StockImportPlannerTests
{
    [Fact]
    public void PositiveAndNegativeVariationsUseCurrentBranchBalances()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var weight = Product(companyId, "2546", ProductSaleMode.Weight);
        var units = Product(companyId, "0007", ProductSaleMode.Unit);
        var weightBalance = Balance(companyId, branchId, weight.Id, 10m, 2m);
        var unitBalance = Balance(companyId, branchId, units.Id, 5m, 0m);

        var plan = StockImportPlanner.Plan(companyId, branchId,
            [Row(2, "2546", -3.125m), Row(3, "0007", 2m, "  Reposición  ")],
            [weight, units], [weightBalance, unitBalance]);

        Assert.Empty(plan.Issues);
        Assert.Equal(2, plan.Changes.Count);
        Assert.Equal(6.875m, plan.Changes[0].OnHandAfter);
        Assert.Equal("Importación Excel", plan.Changes[0].Reason);
        Assert.Equal(7m, plan.Changes[1].OnHandAfter);
        Assert.Equal("Importación Excel: Reposición", plan.Changes[1].Reason);
    }

    [Fact]
    public void RejectsDuplicateArticleAndDecreaseBelowReserved()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var product = Product(companyId, "2546", ProductSaleMode.Weight);
        var balance = Balance(companyId, branchId, product.Id, 10m, 2m);

        var plan = StockImportPlanner.Plan(companyId, branchId,
            [Row(2, "2546", -9m), Row(3, " 2546 ", 1m)], [product], [balance]);

        Assert.Contains(plan.Issues, issue => issue.Code == "STOCK_BELOW_RESERVED" && issue.Row == 2);
        Assert.Contains(plan.Issues, issue => issue.Code == "DUPLICATE_ROW" && issue.Row == 3);
    }

    [Fact]
    public void RejectsFractionalUnitsAndUnknownOrInactiveArticle()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var unit = Product(companyId, "0007", ProductSaleMode.Unit);
        var inactive = Product(companyId, "0008", ProductSaleMode.Weight);
        inactive.Deactivate();

        var plan = StockImportPlanner.Plan(companyId, branchId,
            [Row(2, "0007", 1.5m), Row(3, "0008", 1m), Row(4, "9999", 1m)],
            [unit, inactive], []);

        Assert.Contains(plan.Issues, issue => issue.Code == "INVALID_PRODUCT_QUANTITY" && issue.Row == 2);
        Assert.Contains(plan.Issues, issue => issue.Code == "PRODUCT_NOT_AVAILABLE" && issue.Row == 3);
        Assert.Contains(plan.Issues, issue => issue.Code == "PRODUCT_NOT_AVAILABLE" && issue.Row == 4);
    }

    [Fact]
    public void DoesNotUseAnotherBranchBalanceForADecrease()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var product = Product(companyId, "2546", ProductSaleMode.Weight);
        var otherBranchBalance = Balance(companyId, Guid.NewGuid(), product.Id, 50m, 0m);

        var plan = StockImportPlanner.Plan(companyId, branchId,
            [Row(2, "2546", -1m)], [product], [otherBranchBalance]);

        Assert.Contains(plan.Issues, issue => issue.Code == "STOCK_BELOW_RESERVED");
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void RejectsDescriptionThatWouldOverflowAuditReason()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var product = Product(companyId, "2546", ProductSaleMode.Weight);

        var plan = StockImportPlanner.Plan(companyId, branchId,
            [Row(2, "2546", 1m, new string('a', 222))], [product], []);

        Assert.Contains(plan.Issues, issue => issue.Code == "INVALID_DESCRIPTION");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100001)]
    [InlineData(-100001)]
    [InlineData(1.2345)]
    public void RejectsInvalidVariations(decimal quantity)
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var product = Product(companyId, "2546", ProductSaleMode.Weight);

        var plan = StockImportPlanner.Plan(companyId, branchId,
            [Row(2, "2546", quantity)], [product], []);

        Assert.Contains(plan.Issues, issue => issue.Code == "INVALID_VARIATION");
    }

    private static CatalogProduct Product(Guid companyId, string code, ProductSaleMode saleMode) =>
        new(companyId, Guid.NewGuid(), code, "Artículo", saleMode == ProductSaleMode.Unit ? "u." : "kg",
            saleMode, 100m);

    private static BranchInventoryBalance Balance(Guid companyId, Guid branchId, Guid productId,
        decimal onHand, decimal reserved)
    {
        var balance = new BranchInventoryBalance(companyId, branchId, productId);
        balance.SetQuantities(onHand, reserved);
        return balance;
    }

    private static ImportRow Row(int number, string code, decimal quantity, string? description = null) =>
        new("Existencias", number, new Dictionary<string, ImportCell>
        {
            ["CodigoArticulo"] = new(code, null),
            ["Variacion"] = new(null, quantity),
            ["Descripcion"] = new(description, null)
        });
}
