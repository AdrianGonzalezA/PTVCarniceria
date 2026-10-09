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
        Name = NormalizeName(name);
        Id = Guid.NewGuid();
        CompanyId = companyId;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }

    public void Rename(string name) => Name = NormalizeName(name);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 120)
            throw new ArgumentException("Category name must be between 1 and 120 characters.", nameof(name));
        return normalized;
    }
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

        Id = Guid.NewGuid();
        CompanyId = companyId;
        Code = code.Trim();
        NormalizedCode = Code.ToUpperInvariant();
        UpdateDetails(categoryId, name, unit, saleMode, cost);
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Code { get; private set; }
    public string NormalizedCode { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public ProductSaleMode SaleMode { get; private set; }
    public decimal Cost { get; private set; }
    public bool IsActive { get; private set; }

    public void UpdateDetails(Guid categoryId, string name, string unit, ProductSaleMode saleMode, decimal cost)
    {
        if (categoryId == Guid.Empty) throw new ArgumentException("Category id is required.", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Product name is required and must not exceed 200 characters.", nameof(name));
        if (string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > 24)
            throw new ArgumentException("Product unit is required and must not exceed 24 characters.", nameof(unit));
        if (!Enum.IsDefined(saleMode)) throw new ArgumentOutOfRangeException(nameof(saleMode));
        var roundedCost = decimal.Round(cost, 2, MidpointRounding.AwayFromZero);
        if (roundedCost <= 0 || roundedCost > 9_999_999_999.99m)
            throw new ArgumentOutOfRangeException(nameof(cost), "Product cost must fit a positive 12,2 amount.");

        CategoryId = categoryId;
        Name = name.Trim();
        Unit = unit.Trim();
        SaleMode = saleMode;
        Cost = roundedCost;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
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
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Code { get; private set; }
    public string NormalizedCode { get; private set; }
    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public sealed class ProductCostVersion
{
    private ProductCostVersion() { }

    public ProductCostVersion(Guid companyId, Guid productId, decimal amount,
        DateTimeOffset effectiveFromUtc, Guid changedByUserId)
    {
        if (companyId == Guid.Empty || productId == Guid.Empty || changedByUserId == Guid.Empty)
            throw new ArgumentException("Company, product and user ids are required.");
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded <= 0 || rounded > 9_999_999_999.99m)
            throw new ArgumentOutOfRangeException(nameof(amount));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        ProductId = productId;
        Amount = rounded;
        EffectiveFromUtc = effectiveFromUtc.ToUniversalTime();
        ChangedByUserId = changedByUserId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }
    // Null only for the baseline backfilled from products predating cost history.
    public Guid? ChangedByUserId { get; private set; }

    public void CloseAt(DateTimeOffset effectiveToUtc)
    {
        var end = effectiveToUtc.ToUniversalTime();
        if (end <= EffectiveFromUtc || EffectiveToUtc is not null)
            throw new ArgumentOutOfRangeException(nameof(effectiveToUtc));
        EffectiveToUtc = end;
    }
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
