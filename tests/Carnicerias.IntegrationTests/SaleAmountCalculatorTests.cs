using Carnicerias.Domain.Sales;

namespace Carnicerias.IntegrationTests;

public sealed class SaleAmountCalculatorTests
{
    [Fact]
    public void ProratesOrderDiscountBeforeExtractingTaxFromFinalPrices()
    {
        var taxedId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var exemptId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var result = SaleAmountCalculator.Calculate([
            new SaleAmountInput(taxedId, 1m, 121m, 0m, SaleTaxTreatment.Taxed, 21m),
            new SaleAmountInput(exemptId, 1m, 79m, 0m, SaleTaxTreatment.Exempt, 0m)
        ], 20m);

        Assert.Equal(200m, result.GrossBeforeDiscount);
        Assert.Equal(20m, result.TotalDiscount);
        Assert.Equal(180m, result.Total);
        var taxed = Assert.Single(result.Lines, line => line.LineId == taxedId);
        Assert.Equal(12.10m, taxed.OrderDiscount);
        Assert.Equal(108.90m, taxed.Total);
        Assert.Equal(90m, taxed.TaxableBase);
        Assert.Equal(18.90m, taxed.TaxAmount);
        var exempt = Assert.Single(result.Lines, line => line.LineId == exemptId);
        Assert.Equal(7.90m, exempt.OrderDiscount);
        Assert.Equal(71.10m, exempt.ExemptAmount);
    }

    [Fact]
    public void AllocatesRoundingRemainderDeterministicallyWithoutLosingACent()
    {
        Guid[] ids = [
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002")
        ];
        var result = SaleAmountCalculator.Calculate(ids.Select(id => new SaleAmountInput(
            id, 1m, 1m, 0m, SaleTaxTreatment.Taxed, 10.5m)).ToArray(), 0.01m);

        Assert.Equal(2.99m, result.Total);
        Assert.Equal(0.01m, result.Lines.Sum(line => line.OrderDiscount));
        Assert.Equal(0.01m, result.Lines.Single(line => line.LineId == ids[1]).OrderDiscount);
        Assert.Equal(result.Total, result.Lines.Sum(line => line.Total));
    }

    [Fact]
    public void RejectsDiscountLargerThanAvailablePrice()
    {
        var line = new SaleAmountInput(Guid.NewGuid(), 1m, 100m, 10m,
            SaleTaxTreatment.NotTaxed, 0m);

        Assert.Throws<ArgumentOutOfRangeException>(() => SaleAmountCalculator.Calculate([line], 90.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SaleAmountCalculator.Calculate([
            line with { LineDiscount = 100.01m }
        ], 0m));
    }
}
