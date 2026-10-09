using Carnicerias.Domain.Sales;

namespace Carnicerias.Infrastructure;

public sealed class ProductTaxRule
{
    private ProductTaxRule() { }

    public ProductTaxRule(Guid companyId, Guid productId, SaleTaxTreatment treatment,
        decimal ratePercent, Guid changedByUserId, DateTimeOffset effectiveFromUtc,
        Guid? taxCatalogEntryId = null)
    {
        if (companyId == Guid.Empty || productId == Guid.Empty || changedByUserId == Guid.Empty)
            throw new ArgumentException("Company, product and actor are required.");
        if (!Enum.IsDefined(treatment) || ratePercent is < 0 or > 100 ||
            decimal.Round(ratePercent, 2) != ratePercent ||
            (treatment != SaleTaxTreatment.Taxed && ratePercent != 0))
            throw new ArgumentOutOfRangeException(nameof(ratePercent));
        if (taxCatalogEntryId == Guid.Empty ||
            (treatment != SaleTaxTreatment.Taxed && taxCatalogEntryId is not null))
            throw new ArgumentException("A catalog entry may only be linked to a taxed rule.",
                nameof(taxCatalogEntryId));
        Id = Guid.NewGuid();
        CompanyId = companyId;
        ProductId = productId;
        Treatment = treatment;
        RatePercent = ratePercent;
        TaxCatalogEntryId = taxCatalogEntryId;
        ChangedByUserId = changedByUserId;
        EffectiveFromUtc = effectiveFromUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public SaleTaxTreatment Treatment { get; private set; }
    public decimal RatePercent { get; private set; }
    public Guid? TaxCatalogEntryId { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }

    public void Close(DateTimeOffset atUtc)
    {
        var utc = atUtc.ToUniversalTime();
        if (EffectiveToUtc is not null || utc <= EffectiveFromUtc)
            throw new InvalidOperationException("Only an open tax rule can be closed at a later instant.");
        EffectiveToUtc = utc;
    }
}
