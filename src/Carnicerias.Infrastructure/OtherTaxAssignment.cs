namespace Carnicerias.Infrastructure;

public sealed class OtherTaxAssignment
{
    private OtherTaxAssignment() { }

    public OtherTaxAssignment(Guid companyId, Guid productId, Guid taxCatalogEntryId,
        Guid assignedByUserId, DateTimeOffset effectiveFromUtc)
    {
        if (companyId == Guid.Empty || productId == Guid.Empty ||
            taxCatalogEntryId == Guid.Empty || assignedByUserId == Guid.Empty)
            throw new ArgumentException("Company, product, tax and actor are required.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        ProductId = productId;
        TaxCatalogEntryId = taxCatalogEntryId;
        AssignedByUserId = assignedByUserId;
        EffectiveFromUtc = effectiveFromUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid TaxCatalogEntryId { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }
    public Guid? RemovedByUserId { get; private set; }

    public void Close(DateTimeOffset atUtc, Guid actorId)
    {
        var utc = atUtc.ToUniversalTime();
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        if (EffectiveToUtc is not null || utc <= EffectiveFromUtc)
            throw new InvalidOperationException("Only an open assignment can be closed later.");
        EffectiveToUtc = utc;
        RemovedByUserId = actorId;
    }
}
