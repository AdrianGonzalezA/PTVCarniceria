namespace Carnicerias.PlatformAccess;

public sealed class Company
{
    private Company()
    {
        Name = string.Empty;
        Code = string.Empty;
    }

    public Company(string name) : this(name, Guid.NewGuid().ToString("N")) { }

    public Company(string name, string code)
    {
        Name = NormalizeName(name);
        Code = NormalizeCode(code);
        Id = Guid.NewGuid();
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Code { get; private set; }

    public bool IsActive { get; private set; }

    public void Rename(string name) => Name = NormalizeName(name);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Company name must be between 1 and 200 characters.", nameof(name));
        return name.Trim();
    }

    private static string NormalizeCode(string code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 40 ||
            normalized[0] == '-' || normalized[^1] == '-' ||
            normalized.Any(character => !char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character) && character != '-'))
            throw new ArgumentException("Company code must contain 2–40 ASCII letters, digits or hyphens.", nameof(code));
        return normalized;
    }
}
