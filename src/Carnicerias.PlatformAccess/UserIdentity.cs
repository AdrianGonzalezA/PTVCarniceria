using System.Text;

namespace Carnicerias.PlatformAccess;

public sealed class UserIdentity
{
    private UserIdentity()
    {
        Username = string.Empty;
        UsernameNormalized = string.Empty;
        Email = string.Empty;
        EmailNormalized = string.Empty;
        PasswordHash = string.Empty;
    }

    private UserIdentity(string username, string email, string passwordHash)
    {
        var normalizedUsername = Normalize(username);
        var normalizedEmail = Normalize(email);
        if (normalizedUsername.Length == 0 || normalizedUsername.Length > 100)
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (normalizedEmail.Length == 0 || normalizedEmail.Length > 320)
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        Id = Guid.NewGuid();
        Username = normalizedUsername;
        UsernameNormalized = normalizedUsername.ToUpperInvariant();
        Email = normalizedEmail;
        EmailNormalized = normalizedEmail.ToLowerInvariant();
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Username { get; private set; }

    public string UsernameNormalized { get; private set; }

    public string Email { get; private set; }

    public string EmailNormalized { get; private set; }

    public string PasswordHash { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void UpdateDetails(string username, string email)
    {
        var normalizedUsername = Normalize(username);
        var normalizedEmail = Normalize(email);
        if (normalizedUsername.Length == 0 || normalizedUsername.Length > 100 ||
            normalizedEmail.Length == 0 || normalizedEmail.Length > 320)
        {
            throw new ArgumentException("Username and email are required.");
        }

        Username = normalizedUsername;
        UsernameNormalized = normalizedUsername.ToUpperInvariant();
        Email = normalizedEmail;
        EmailNormalized = normalizedEmail.ToLowerInvariant();
    }

    public static UserIdentity Create(string username, string email, string passwordHash) =>
        new(username, email, passwordHash);

    private static string Normalize(string value) =>
        (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
}
