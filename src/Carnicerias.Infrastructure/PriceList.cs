namespace Carnicerias.Infrastructure;

public sealed class PriceList
{
    private PriceList() => Name = string.Empty;

    public PriceList(Guid companyId, string name)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Price list name is required.", nameof(name))
            : name.Trim();
        Id = Guid.NewGuid();
        CompanyId = companyId;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }
}

public sealed class BranchPriceList
{
    private BranchPriceList() { }

    public BranchPriceList(Guid companyId, Guid branchId, Guid priceListId)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || priceListId == Guid.Empty)
        {
            throw new ArgumentException("Company, branch and price list ids are required.");
        }

        CompanyId = companyId;
        BranchId = branchId;
        PriceListId = priceListId;
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid PriceListId { get; private set; }

    public bool IsActive { get; private set; }
}
