using System.Text;

namespace Carnicerias.Infrastructure;

public sealed class CustomerAccount
{
    private CustomerAccount()
    {
        Code = string.Empty;
        NormalizedCode = string.Empty;
        Name = string.Empty;
    }

    public CustomerAccount(Guid companyId, string code, string name)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company id is required.", nameof(companyId));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        UpdateDetails(code, name);
        IsActive = true;
        CreditEnabled = false;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public bool CreditEnabled { get; private set; }
    public bool CanChargeToAccount => IsActive && CreditEnabled;

    public void UpdateDetails(string code, string name)
    {
        var trimmedCode = code?.Trim().Normalize(NormalizationForm.FormKC);
        var trimmedName = name?.Trim().Normalize(NormalizationForm.FormKC);
        if (string.IsNullOrWhiteSpace(trimmedCode) || trimmedCode.Length > 80)
            throw new ArgumentException("Customer code must be between 1 and 80 characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 200)
            throw new ArgumentException("Customer name must be between 1 and 200 characters.", nameof(name));

        Code = trimmedCode;
        NormalizedCode = trimmedCode.ToUpperInvariant();
        Name = trimmedName;
    }

    public void EnableCredit()
    {
        if (!IsActive)
            throw new InvalidOperationException("Inactive customers cannot be enabled for credit.");
        CreditEnabled = true;
    }

    public void DisableCredit() => CreditEnabled = false;

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        IsActive = false;
        CreditEnabled = false;
    }
}
