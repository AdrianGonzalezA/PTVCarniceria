using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.ArchitectureTests;

public sealed class ProductScalePluTests
{
    [Fact]
    public void ProductKeepsAnOptionalNumericScalePlu()
    {
        var product = new CatalogProduct(Guid.NewGuid(), Guid.NewGuid(),
            "ASADO", "Asado", "kg", ProductSaleMode.Weight, null);

        product.SetScalePlu("00123");
        Assert.Equal("00123", product.ScalePlu);
        Assert.Throws<ArgumentException>(() => product.SetScalePlu("12A3"));
        product.SetScalePlu(null);
        Assert.Null(product.ScalePlu);
    }

    [Fact]
    public void ScalePluIsUniqueWithinEachCompany()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options;
        using var db = new PlatformAccessDbContext(options);
        var entity = db.Model.FindEntityType(typeof(CatalogProduct))!;
        var index = Assert.Single(entity.GetIndexes(), item => item.IsUnique &&
            item.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "ScalePlu"]));
        Assert.NotNull(index);
    }
}
