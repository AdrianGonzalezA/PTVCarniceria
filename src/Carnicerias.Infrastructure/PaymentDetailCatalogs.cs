namespace Carnicerias.Infrastructure;

public sealed class PaymentCardBrand
{
    private PaymentCardBrand() { }

    public PaymentCardBrand(string code, string name, bool supportsDebit, bool supportsCredit)
    {
        if (!supportsDebit && !supportsCredit) throw new ArgumentException("At least one card type is required.");
        Code = PaymentCatalogName.Code(code);
        Name = PaymentCatalogName.Name(name);
        SupportsDebit = supportsDebit;
        SupportsCredit = supportsCredit;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool SupportsDebit { get; private set; }
    public bool SupportsCredit { get; private set; }
    public bool IsActive { get; private set; } = true;
}

public sealed class PaymentProcessor
{
    private PaymentProcessor() { }
    public PaymentProcessor(string code, string name)
    {
        Code = PaymentCatalogName.Code(code);
        Name = PaymentCatalogName.Name(name);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
}

public sealed class FinancialInstitution
{
    private FinancialInstitution() { }
    public FinancialInstitution(string code, string name)
    {
        Code = PaymentCatalogName.Code(code);
        Name = PaymentCatalogName.Name(name);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
}

public sealed class PaymentWallet
{
    private PaymentWallet() { }
    public PaymentWallet(string code, string name)
    {
        Code = PaymentCatalogName.Code(code);
        Name = PaymentCatalogName.Name(name);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
}

internal static class PaymentCatalogName
{
    public static string Code(string value)
    {
        var code = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40 ||
            code.Any(character => !char.IsAsciiLetterLower(character) &&
                !char.IsAsciiDigit(character) && character != '-'))
            throw new ArgumentException("Catalog code must contain 1-40 ASCII letters, digits or '-'.", nameof(value));
        return code;
    }

    public static string Name(string value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new ArgumentException("Catalog name must contain 1-100 characters.", nameof(value));
        return name;
    }
}
