namespace Carnicerias.Infrastructure;

public enum ProductSaleMode
{
    Weight,
    Unit
}

public sealed class ProductCategory
{
    private ProductCategory() => Name = string.Empty;

    public ProductCategory(Guid companyId, string name)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Category name is required.", nameof(name))
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

public sealed class CatalogProduct
{
    private CatalogProduct()
    {
        Code = string.Empty;
        NormalizedCode = string.Empty;
        Name = string.Empty;
        Unit = string.Empty;
    }

    public CatalogProduct(
        Guid companyId,
        Guid categoryId,
        string code,
        string name,
        string unit,
        ProductSaleMode saleMode,
        decimal cost)
    {
        if (companyId == Guid.Empty || categoryId == Guid.Empty)
            throw new ArgumentException("Company and category ids are required.");
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 80)
            throw new ArgumentException("Product code is required and must not exceed 80 characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Product name is required and must not exceed 200 characters.", nameof(name));
        if (string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > 24)
            throw new ArgumentException("Product unit is required and must not exceed 24 characters.", nameof(unit));
        if (!Enum.IsDefined(saleMode)) throw new ArgumentOutOfRangeException(nameof(saleMode));
        if (cost <= 0) throw new ArgumentOutOfRangeException(nameof(cost), "Product cost must be positive.");

        Id = Guid.NewGuid();
        CompanyId = companyId;
        CategoryId = categoryId;
        Code = code.Trim();
        NormalizedCode = Code.ToUpperInvariant();
        Name = name.Trim();
        Unit = unit.Trim();
        SaleMode = saleMode;
        Cost = decimal.Round(cost, 2, MidpointRounding.AwayFromZero);
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Code { get; private set; }
    public string NormalizedCode { get; private set; }
    public string Name { get; private set; }
    public string Unit { get; private set; }
    public ProductSaleMode SaleMode { get; private set; }
    public decimal Cost { get; private set; }
    public bool IsActive { get; private set; }
}

public sealed class ProductCode
{
    private ProductCode()
    {
        Code = string.Empty;
        NormalizedCode = string.Empty;
    }

    public ProductCode(Guid companyId, Guid productId, string code)
    {
        if (companyId == Guid.Empty || productId == Guid.Empty)
            throw new ArgumentException("Company and product ids are required.");
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 80)
            throw new ArgumentException("Product code is required and must not exceed 80 characters.", nameof(code));

        CompanyId = companyId;
        ProductId = productId;
        Code = code.Trim();
        NormalizedCode = Code.ToUpperInvariant();
    }

    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Code { get; private set; }
    public string NormalizedCode { get; private set; }
}

public sealed class ProductPrice
{
    private ProductPrice() { }

    public ProductPrice(
        Guid companyId,
        Guid priceListId,
        Guid productId,
        decimal amount,
        DateTimeOffset effectiveFromUtc,
        Guid changedByUserId)
    {
        if (companyId == Guid.Empty || priceListId == Guid.Empty || productId == Guid.Empty || changedByUserId == Guid.Empty)
            throw new ArgumentException("Company, list, product and user ids are required.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Price must be positive.");

        Id = Guid.NewGuid();
        CompanyId = companyId;
        PriceListId = priceListId;
        ProductId = productId;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        EffectiveFromUtc = effectiveFromUtc.ToUniversalTime();
        ChangedByUserId = changedByUserId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PriceListId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }
    public Guid ChangedByUserId { get; private set; }

    public void CloseAt(DateTimeOffset effectiveToUtc)
    {
        var end = effectiveToUtc.ToUniversalTime();
        if (end <= EffectiveFromUtc || EffectiveToUtc is not null)
            throw new ArgumentOutOfRangeException(nameof(effectiveToUtc));
        EffectiveToUtc = end;
    }
}
