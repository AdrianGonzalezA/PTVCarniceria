namespace Carnicerias.Central.Infrastructure;

public sealed class CentralCompany
{
    private CentralCompany()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private CentralCompany(string code, string name)
    {
        Id = Guid.NewGuid();
        Code = code;
        Name = name;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }

    public static CentralCompany Create(string? code, string? name)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedCode.Length is < 2 or > 40 ||
            normalizedCode.Any(character => !char.IsAsciiLetterUpper(character) &&
                                            !char.IsAsciiDigit(character) && character != '-'))
            throw new ArgumentException("Invalid central company code.", nameof(code));
        if (normalizedName.Length is < 1 or > 200)
            throw new ArgumentException("Invalid central company name.", nameof(name));
        return new CentralCompany(normalizedCode, normalizedName);
    }

    public void Deactivate() => IsActive = false;
}
