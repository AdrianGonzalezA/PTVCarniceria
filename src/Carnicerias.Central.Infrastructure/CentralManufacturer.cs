using System.Text;

namespace Carnicerias.Central.Infrastructure;

public sealed class CentralManufacturer
{
    private CentralManufacturer()
    {
        Username = string.Empty;
        UsernameNormalized = string.Empty;
        PasswordHash = string.Empty;
    }

    private CentralManufacturer(string username, string passwordHash)
    {
        Id = Guid.NewGuid();
        Username = username;
        UsernameNormalized = username.ToUpperInvariant();
        PasswordHash = passwordHash;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Username { get; private set; }
    public string UsernameNormalized { get; private set; }
    public string PasswordHash { get; private set; }
    public bool IsActive { get; private set; }

    public static CentralManufacturer Create(string? username, string? passwordHash)
    {
        var normalized = (username ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > 100 || normalized.Contains('@') ||
            normalized.Any(char.IsControl))
            throw new ArgumentException("Invalid central manufacturer username.", nameof(username));
        if (string.IsNullOrWhiteSpace(passwordHash) ||
            !passwordHash.StartsWith("$argon2id$", StringComparison.Ordinal))
            throw new ArgumentException("Argon2id password hash is required.", nameof(passwordHash));
        return new CentralManufacturer(normalized, passwordHash);
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) ||
            !passwordHash.StartsWith("$argon2id$", StringComparison.Ordinal))
            throw new ArgumentException("Argon2id password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
    }

    public void Deactivate() => IsActive = false;
}
