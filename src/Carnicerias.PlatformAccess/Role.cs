namespace Carnicerias.PlatformAccess;

public sealed class Role
{
    private Role()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Role(string code, string name)
    {
        Code = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("Role code is required.", nameof(code))
            : code.Trim();
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Role name is required.", nameof(name))
            : name.Trim();
        Id = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }
}
