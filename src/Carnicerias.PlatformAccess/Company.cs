namespace Carnicerias.PlatformAccess;

public sealed class Company
{
    private Company() => Name = string.Empty;

    public Company(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Company name is required.", nameof(name))
            : name.Trim();
        Id = Guid.NewGuid();
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }
}
