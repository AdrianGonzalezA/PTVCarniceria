using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class ProductTaxRuleTests
{
    [Fact]
    public void KeepsEffectiveHistoryWhenChangingTheProductsTaxTreatment()
    {
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var change = DateTimeOffset.UtcNow;
        var previous = new ProductTaxRule(companyId, productId, SaleTaxTreatment.Taxed,
            21m, actorId, start);

        previous.Close(change);
        var current = new ProductTaxRule(companyId, productId, SaleTaxTreatment.Exempt,
            0m, actorId, change);

        Assert.Equal(change, previous.EffectiveToUtc);
        Assert.Null(current.EffectiveToUtc);
        Assert.Equal(SaleTaxTreatment.Exempt, current.Treatment);
        Assert.Throws<InvalidOperationException>(() => previous.Close(change.AddMinutes(1)));
    }

    [Theory]
    [InlineData(SaleTaxTreatment.Exempt, 21)]
    [InlineData(SaleTaxTreatment.NotTaxed, 10.5)]
    [InlineData(SaleTaxTreatment.Taxed, -1)]
    [InlineData(SaleTaxTreatment.Taxed, 101)]
    public void RejectsInvalidTreatmentAndRateCombinations(SaleTaxTreatment treatment, decimal rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductTaxRule(
            Guid.NewGuid(), Guid.NewGuid(), treatment, rate, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void KeepsCatalogEntryReferenceOnTaxedRule()
    {
        var taxId = Guid.NewGuid();
        var rule = new ProductTaxRule(Guid.NewGuid(), Guid.NewGuid(), SaleTaxTreatment.Taxed,
            21m, Guid.NewGuid(), DateTimeOffset.UtcNow, taxId);

        Assert.Equal(taxId, rule.TaxCatalogEntryId);
    }

    [Fact]
    public void RejectsCatalogEntryOnExemptRule()
    {
        Assert.Throws<ArgumentException>(() => new ProductTaxRule(Guid.NewGuid(), Guid.NewGuid(),
            SaleTaxTreatment.Exempt, 0m, Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()));
    }
}
