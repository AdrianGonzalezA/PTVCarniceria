using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class AnatomicalGroupTests
{
    [Fact]
    public void ClassificationIsOptionalNormalizedAndIndependentOfSaleMode()
    {
        var product = new CatalogProduct(Guid.NewGuid(), Guid.NewGuid(),
            "LOMO-1", "Lomo limpio", "kg", ProductSaleMode.Weight, null);
        Assert.Null(product.AnatomicalGroup);
        product.SetAnatomicalGroup("  Lomo  ");
        Assert.Equal("Lomo", product.AnatomicalGroup);
        Assert.Equal("LOMO", product.AnatomicalGroupNormalized);
        Assert.Equal(ProductSaleMode.Weight, product.SaleMode);
        product.SetAnatomicalGroup("  ");
        Assert.Null(product.AnatomicalGroup);
        Assert.Null(product.AnatomicalGroupNormalized);
    }

    [Fact]
    public void OverlongClassificationIsRejectedWithoutChangingExistingValue()
    {
        var product = new CatalogProduct(Guid.NewGuid(), Guid.NewGuid(),
            "LOMO-2", "Lomo completo", "kg", ProductSaleMode.Weight, null);
        product.SetAnatomicalGroup("Lomo");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            product.SetAnatomicalGroup(new string('X', 121)));
        Assert.Equal("Lomo", product.AnatomicalGroup);
    }

    [Fact]
    public void PhysicalCountCapturesTheGroupAtMeasurementTime()
    {
        var product = new CatalogProduct(Guid.NewGuid(), Guid.NewGuid(),
            "LOMO-3", "Lomo limpio", "kg", ProductSaleMode.Weight, null);
        product.SetAnatomicalGroup("Lomo");
        var line = new PhysicalStockCountLine(Guid.NewGuid(), product,
            StockCountPolicy.MassObservation, 2.5m, 0m, 0m, 0m, 0m, 0m,
            null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        product.SetAnatomicalGroup("Otro");
        Assert.Equal("Lomo", line.AnatomicalGroup);
        Assert.Equal("Otro", product.AnatomicalGroup);
    }
}
