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
    }
}
