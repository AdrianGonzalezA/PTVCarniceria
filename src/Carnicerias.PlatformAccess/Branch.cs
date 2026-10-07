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

        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Branch name is required.", nameof(name))
            : name.Trim();
        Id = Guid.NewGuid();
        CompanyId = companyId;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }
}
