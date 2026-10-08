using Carnicerias.Domain.Sales;

namespace Carnicerias.IntegrationTests;

public sealed class CustomerCollectionPlanTests
{
    private static readonly Guid OlderSale = Guid.NewGuid();
    private static readonly Guid NewerSale = Guid.NewGuid();
    private static readonly DateTimeOffset OlderDate = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NewerDate = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly OutstandingAccountSale[] Sales =
    [
        new(NewerSale, NewerDate, 800m),
        new(OlderSale, OlderDate, 1000m)
    ];

    [Fact]
    public void SuggestsOldestSalesAndLeavesExcessAsCustomerCredit()
    {
        var plan = CustomerCollectionPlan.Suggest(2000m, Sales);

        Assert.Equal([(OlderSale, 1000m), (NewerSale, 800m)],
            plan.Allocations.Select(item => (item.SaleId, item.Amount)));
        Assert.Equal(200m, plan.CreditAmount);
    }

    [Fact]
    public void AllowsCashierToApplyOnlyPartOfOneSale()
    {
        var plan = CustomerCollectionPlan.Choose(600m, Sales,
            [new AccountAllocationChoice(NewerSale, 500m)]);

        var allocation = Assert.Single(plan.Allocations);
        Assert.Equal((NewerSale, 500m), (allocation.SaleId, allocation.Amount));
        Assert.Equal(100m, plan.CreditAmount);
    }

    [Fact]
    public void RejectsUnknownDuplicateOrOverAllocatedSales()
    {
        Assert.Throws<AccountAllocationException>(() => CustomerCollectionPlan.Choose(500m, Sales,
            [new AccountAllocationChoice(Guid.NewGuid(), 500m)]));
        Assert.Throws<AccountAllocationException>(() => CustomerCollectionPlan.Choose(500m, Sales,
            [new AccountAllocationChoice(OlderSale, 200m), new AccountAllocationChoice(OlderSale, 300m)]));
        Assert.Throws<AccountAllocationException>(() => CustomerCollectionPlan.Choose(1100m, Sales,
            [new AccountAllocationChoice(OlderSale, 1100m)]));
        Assert.Throws<AccountAllocationException>(() => CustomerCollectionPlan.Choose(500m, Sales,
            [new AccountAllocationChoice(OlderSale, 501m)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.001)]
    public void RejectsInvalidCollectionAmount(decimal amount)
    {
        Assert.Throws<AccountAllocationException>(() => CustomerCollectionPlan.Suggest(amount, Sales));
    }
}
