namespace Carnicerias.PlatformAccess;

public sealed class PermissionDefinition
{
    private PermissionDefinition()
    {
        Code = string.Empty;
        Description = string.Empty;
    }

    public PermissionDefinition(string code, string description)
    {
        Code = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("Permission code is required.", nameof(code))
            : code.Trim();
        Description = string.IsNullOrWhiteSpace(description)
            ? throw new ArgumentException("Permission description is required.", nameof(description))
            : description.Trim();
        Id = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Description { get; private set; }
}
