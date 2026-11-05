using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class I10ProductFlagTests
{
    [Fact]
    public void NewProductAllowsSaleWithoutStockUntilAdministratorDisablesIt()
    {
        var product = new CatalogProduct(Guid.NewGuid(), Guid.NewGuid(), "A1", "Asado",
            "kg", ProductSaleMode.Weight, null);

        Assert.True(product.AllowSaleWithoutStock);
        product.SetAllowSaleWithoutStock(false);
        Assert.False(product.AllowSaleWithoutStock);
        product.SetAllowSaleWithoutStock(true);
        Assert.True(product.AllowSaleWithoutStock);
    }

    [Fact]
    public void ProtectedPieceDoesNotCoverGenericDebt()
    {
        var balance = new BranchInventoryBalance(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        balance.SetQuantities(-3m, 0m, protectedOnHand: 2m, allowGenericDeficit: true);

        Assert.Equal(-5m, balance.GenericOnHand);
        Assert.Equal(-5m, balance.GenericAvailable);
        Assert.Equal(2m, balance.ProtectedAvailable);
        Assert.Equal(-3m, balance.OnHand);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            balance.SetQuantities(-3m, 0m, protectedOnHand: 1m,
                protectedReserved: 2m, allowGenericDeficit: true));
    }
}
