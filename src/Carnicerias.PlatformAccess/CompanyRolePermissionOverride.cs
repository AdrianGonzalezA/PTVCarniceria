namespace Carnicerias.PlatformAccess;

/// <summary>Company-specific grant or denial over a shared role's default permission.</summary>
public sealed class CompanyRolePermissionOverride
{
    private CompanyRolePermissionOverride() { }

    public CompanyRolePermissionOverride(Guid companyId, Guid roleId, Guid permissionId,
        bool isGranted, Guid updatedByUserId, DateTimeOffset updatedAtUtc)
    {
        if (companyId == Guid.Empty || roleId == Guid.Empty ||
            permissionId == Guid.Empty || updatedByUserId == Guid.Empty)
            throw new ArgumentException("Company, role, permission and actor are required.");
        CompanyId = companyId;
        RoleId = roleId;
        PermissionId = permissionId;
        IsGranted = isGranted;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public Guid CompanyId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public bool IsGranted { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Set(bool isGranted, Guid actorUserId, DateTimeOffset atUtc)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor is required.");
        IsGranted = isGranted;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = atUtc.ToUniversalTime();
    }
}
