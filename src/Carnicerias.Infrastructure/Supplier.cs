using System.Text;

namespace Carnicerias.Infrastructure;

/// <summary>Company-scoped supplier master; receipts copy its identity at creation time.</summary>
public sealed class Supplier
{
    private Supplier()
    {
        Code = string.Empty;
        NormalizedCode = string.Empty;
        Name = string.Empty;
    }

    public Supplier(Guid companyId, string code, string name, string? identification = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        Id = Guid.NewGuid();
        CompanyId = companyId;
        UpdateDetails(code, name, identification);
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Identification { get; private set; }
    public bool IsActive { get; private set; }

    public void UpdateDetails(string code, string name, string? identification)
    {
        var normalizedCode = NormalizeRequired(code, 80, nameof(code));
        var normalizedName = NormalizeRequired(name, 200, nameof(name));
        var normalizedIdentification = identification?.Trim().Normalize(NormalizationForm.FormKC);
        if (normalizedIdentification?.Length > 40)
            throw new ArgumentException("Supplier identification exceeds 40 characters.",
                nameof(identification));
        Code = normalizedCode;
        NormalizedCode = normalizedCode.ToUpperInvariant();
        Name = normalizedName;
        Identification = string.IsNullOrWhiteSpace(normalizedIdentification)
            ? null : normalizedIdentification;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private static string NormalizeRequired(string? value, int maximum, string name)
    {
        var normalized = value?.Trim().Normalize(NormalizationForm.FormKC);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximum)
            throw new ArgumentException($"Supplier {name} is required and must fit its limit.", name);
        return normalized;
    }
}
