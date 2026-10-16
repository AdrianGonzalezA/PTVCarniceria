using System.Text;

namespace Carnicerias.Central.Infrastructure;

public sealed class CentralAdministrator
{
    private CentralAdministrator()
    {
        Username = string.Empty;
        UsernameNormalized = string.Empty;
        PasswordHash = string.Empty;
    }

    private CentralAdministrator(Guid centralCompanyId, string username, string passwordHash)
    {
        Id = Guid.NewGuid();
        CentralCompanyId = centralCompanyId;
        Username = username;
        UsernameNormalized = username.ToUpperInvariant();
        PasswordHash = passwordHash;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid CentralCompanyId { get; private set; }
    public string Username { get; private set; }
    public string UsernameNormalized { get; private set; }
    public string PasswordHash { get; private set; }
    public bool IsActive { get; private set; }

    public static CentralAdministrator Create(Guid centralCompanyId, string? username, string? passwordHash)
    {
        var normalizedUsername = (username ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
        if (centralCompanyId == Guid.Empty)
            throw new ArgumentException("Central company is required.", nameof(centralCompanyId));
        if (normalizedUsername.Length is < 1 or > 100 || normalizedUsername.Contains('@') ||
            normalizedUsername.Any(char.IsControl))
            throw new ArgumentException("Invalid central administrator username.", nameof(username));
        if (string.IsNullOrWhiteSpace(passwordHash) || !passwordHash.StartsWith("$argon2id$", StringComparison.Ordinal))
            throw new ArgumentException("Argon2id password hash is required.", nameof(passwordHash));
        return new CentralAdministrator(centralCompanyId, normalizedUsername, passwordHash);
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || !passwordHash.StartsWith("$argon2id$", StringComparison.Ordinal))
            throw new ArgumentException("Argon2id password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
    }

    public void Deactivate() => IsActive = false;
}
