namespace Carnicerias.Infrastructure;

public sealed class ProductionClassifier
{
    private ProductionClassifier() => Name = NormalizedName = string.Empty;

    public ProductionClassifier(Guid companyId, string name)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        Id = Guid.NewGuid();
        CompanyId = companyId;
        Rename(name);
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public void Rename(string name)
    {
        var value = name?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120)
            throw new ArgumentException("Classifier name must be between 1 and 120 characters.", nameof(name));
        Name = value;
        NormalizedName = value.ToUpperInvariant();
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
