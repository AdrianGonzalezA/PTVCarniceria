namespace Carnicerias.PlatformAccess;

public sealed class UserAssignment
{
    private UserAssignment() { }

    public UserAssignment(Guid userId, Guid roleId, Guid companyId, Guid? branchId = null)
    {
        UserId = RequireId(userId, nameof(userId));
        RoleId = RequireId(roleId, nameof(roleId));
        CompanyId = RequireId(companyId, nameof(companyId));
        if (branchId == Guid.Empty)
        {
            throw new ArgumentException("Branch id must be omitted or valid.", nameof(branchId));
        }

        Id = Guid.NewGuid();
        BranchId = branchId;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid? BranchId { get; private set; }

    private static Guid RequireId(Guid value, string parameterName) => value == Guid.Empty
        ? throw new ArgumentException("Identifier is required.", parameterName)
        : value;
}
