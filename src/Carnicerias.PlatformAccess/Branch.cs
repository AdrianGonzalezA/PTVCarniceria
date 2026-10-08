namespace Carnicerias.PlatformAccess;

public sealed class Branch
{
    private Branch() => Name = string.Empty;

    public Branch(Guid companyId, string name)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

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
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Branch name must be between 1 and 200 characters.", nameof(name));
        return name.Trim();
    }
}
