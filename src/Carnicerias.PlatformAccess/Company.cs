namespace Carnicerias.PlatformAccess;

public sealed class Company
{
    private Company() => Name = string.Empty;

    public Company(string name)
    {
        Name = NormalizeName(name);
        Id = Guid.NewGuid();
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

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
}
