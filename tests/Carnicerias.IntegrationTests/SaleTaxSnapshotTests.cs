using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class SaleTaxSnapshotTests
{
    [Fact]
    public void ConfirmedLineKeepsTheEffectiveRuleAndAllocatedDiscount()
    {
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var rule = new ProductTaxRule(companyId, productId, SaleTaxTreatment.Taxed,
            21m, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var line = new ConfirmedSaleLine(companyId, productId, "P1", "Pan", "unidad",
            ProductSaleMode.Unit, 1m, 121m);
        var calculation = SaleAmountCalculator.Calculate([
            new SaleAmountInput(line.Id, 1m, 121m, 0m, rule.Treatment, rule.RatePercent)
        ], 12.10m).Lines[0];

        line.SetAmountSnapshot(calculation, rule);
        rule.Close(DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(12.10m, line.OrderDiscountAmount);
        Assert.Equal(108.90m, line.NetAfterDiscount);
        Assert.Equal(rule.Id, line.TaxRuleId);
        Assert.Equal(SaleTaxTreatment.Taxed, line.TaxTreatment);
        Assert.Equal(21m, line.TaxRatePercent);
        Assert.Equal(90m, line.TaxableBase);
        Assert.Equal(18.90m, line.TaxAmount);
    }

    [Fact]
    public void UnconfiguredProductDoesNotAcquireAnInventedTaxClassification()
    {
        var companyId = Guid.NewGuid();
        var line = new ConfirmedSaleLine(companyId, Guid.NewGuid(), "P1", "Pan", "unidad",
            ProductSaleMode.Unit, 1m, 100m);
        var calculation = SaleAmountCalculator.Calculate([
            new SaleAmountInput(line.Id, 1m, 100m, 0m, SaleTaxTreatment.NotTaxed, 0m)
        ], 5m).Lines[0];

        line.SetAmountSnapshot(calculation, null);

        Assert.Equal(95m, line.NetAfterDiscount);
        Assert.Equal(5m, line.OrderDiscountAmount);
        Assert.Null(line.TaxRuleId);
        Assert.Null(line.TaxTreatment);
        Assert.Null(line.TaxAmount);
    }
}
