using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class ProductManagementTests
{
    [Fact]
    public void UpdatingDetailsPreservesPrincipalCodeAndIdentity()
    {
        var companyId = Guid.NewGuid();
        var product = new CatalogProduct(companyId, Guid.NewGuid(), "VAC-001", "Asado", "kg",
            ProductSaleMode.Weight, 100m);
        var id = product.Id;

        product.UpdateDetails(Guid.NewGuid(), "  Asado especial  ", "  un  ", ProductSaleMode.Unit, 150.125m);

        Assert.Equal(id, product.Id);
        Assert.Equal("VAC-001", product.Code);
        Assert.Equal("Asado especial", product.Name);
        Assert.Equal("un", product.Unit);
        Assert.Equal(ProductSaleMode.Unit, product.SaleMode);
        Assert.Equal(150.13m, product.Cost);
    }

    [Fact]
    public void ProductCanBeInactivatedWithoutChangingCode()
    {
        var product = new CatalogProduct(Guid.NewGuid(), Guid.NewGuid(), "VAC-001", "Asado", "kg",
            ProductSaleMode.Weight, 100m);

        product.Deactivate();
        Assert.False(product.IsActive);
        product.Activate();

        Assert.True(product.IsActive);
        Assert.Equal("VAC-001", product.Code);
    }

    [Fact]
    public void ProductCanStartWithoutCostAndReceiveItsFirstKnownCostLater()
    {
        var product = new CatalogProduct(Guid.NewGuid(), Guid.NewGuid(), "VAC-002",
            "Aguja", "kg", ProductSaleMode.Weight, null);

        Assert.Null(product.Cost);
        product.UpdateDetails(product.CategoryId, product.Name, product.Unit,
            product.SaleMode, 145.125m);
        Assert.Equal(145.13m, product.Cost);
    }

    [Theory]
    [InlineData("", "kg", 100)]
    [InlineData("Asado", "", 100)]
    [InlineData("Asado", "kg", 0)]
    public void UpdatingDetailsRejectsInvalidValues(string name, string unit, decimal cost)
    {
        var product = new CatalogProduct(Guid.NewGuid(), Guid.NewGuid(), "VAC-001", "Asado", "kg",
            ProductSaleMode.Weight, 100m);

        Assert.ThrowsAny<ArgumentException>(() =>
            product.UpdateDetails(Guid.NewGuid(), name, unit, ProductSaleMode.Weight, cost));
    }

    [Fact]
    public void AlternateCodeCanBeDeactivatedAndReactivatedWithoutChangingItsValue()
    {
        var code = new ProductCode(Guid.NewGuid(), Guid.NewGuid(), "  779123456  ");

        code.Deactivate();
        Assert.False(code.IsActive);
        code.Activate();

        Assert.True(code.IsActive);
        Assert.Equal("779123456", code.Code);
        Assert.Equal("779123456", code.NormalizedCode);
        Assert.Equal(ProductCodeKind.SaleAlternative, code.Kind);
        Assert.Null(code.UnitsPerPackage);
    }

    [Fact]
    public void PackageCodeCapturesAnIntegerFactorAndCannotBecomeAnAlternative()
    {
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var code = new ProductCode(companyId, productId, "  779000006  ",
            ProductCodeKind.PurchasePackage, 6);

        Assert.Equal("779000006", code.Code);
        Assert.Equal(ProductCodeKind.PurchasePackage, code.Kind);
        Assert.Equal(6, code.UnitsPerPackage);
        code.ChangePackageFactor(12);
        Assert.Equal(12, code.UnitsPerPackage);
        Assert.ThrowsAny<ArgumentException>(() => code.ChangePackageFactor(1));
        Assert.ThrowsAny<ArgumentException>(() => new ProductCode(companyId, productId,
            "779000001", ProductCodeKind.PurchasePackage, 1));
        Assert.ThrowsAny<ArgumentException>(() => new ProductCode(companyId, productId,
            "779000002", ProductCodeKind.SaleAlternative, 6));
        Assert.Throws<InvalidOperationException>(() =>
            new ProductCode(companyId, productId, "ALT").ChangePackageFactor(6));
    }

    [Fact]
    public void CostVersionClosesWithoutLosingItsOriginalAmount()
    {
        var from = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var until = from.AddMinutes(1);
        var version = new ProductCostVersion(Guid.NewGuid(), Guid.NewGuid(),
            100.125m, from, Guid.NewGuid());

        version.CloseAt(until);

        Assert.Equal(100.13m, version.Amount);
        Assert.Equal(from, version.EffectiveFromUtc);
        Assert.Equal(until, version.EffectiveToUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => version.CloseAt(until.AddMinutes(1)));
    }
}
