namespace Carnicerias.Infrastructure;

public sealed class PosTerminal
{
    private PosTerminal() => Name = string.Empty;

    public PosTerminal(Guid companyId, Guid branchId, string name, bool isHistorical = false)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Company and branch ids are required.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Terminal name must be between 1 and 120 characters.", nameof(name));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        Name = name.Trim();
        IsActive = !isHistorical;
        IsHistorical = isHistorical;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsHistorical { get; private set; }
}
