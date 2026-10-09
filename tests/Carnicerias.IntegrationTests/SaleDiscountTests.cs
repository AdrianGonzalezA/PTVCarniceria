using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class SaleDiscountTests
{
    [Fact]
    public void DraftKeepsAReasonedDiscountAcrossQuantityUpdates()
    {
        var companyId = Guid.NewGuid();
        var draft = new SaleDraft(companyId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), DateTimeOffset.UtcNow);
        var productId = Guid.NewGuid();
        draft.ReplaceLines([new SaleDraftLine(companyId, productId, "A", "Asado", "kg",
            ProductSaleMode.Weight, 1m, 1000m)], DateTimeOffset.UtcNow);

        draft.SetDiscount(100m, "Promoción de mostrador");
        draft.ReplaceLines([new SaleDraftLine(companyId, productId, "A", "Asado", "kg",
            ProductSaleMode.Weight, 2m, 1000m)], DateTimeOffset.UtcNow);

        Assert.Equal(100m, draft.DiscountAmount);
        Assert.Equal("Promoción de mostrador", draft.DiscountReason);
        Assert.Equal(1900m, draft.TotalAfterDiscount);
    }

    [Fact]
    public void RejectsAnUnreasonedOrExcessiveDiscount()
    {
        var companyId = Guid.NewGuid();
        var draft = new SaleDraft(companyId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), DateTimeOffset.UtcNow);
        draft.ReplaceLines([new SaleDraftLine(companyId, Guid.NewGuid(), "A", "Asado", "kg",
            ProductSaleMode.Weight, 1m, 100m)], DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => draft.SetDiscount(10m, "corto"));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.SetDiscount(100m, "Regalo no autorizado"));
        draft.SetDiscount(10m, "Promoción válida");
        draft.SetDiscount(0m, null);
        Assert.Equal(100m, draft.TotalAfterDiscount);
        Assert.Null(draft.DiscountReason);
    }
}
