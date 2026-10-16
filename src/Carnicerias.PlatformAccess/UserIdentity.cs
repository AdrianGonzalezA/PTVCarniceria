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

    private UserIdentity(string username, string email, string passwordHash, Guid? companyId, bool isManufacturer)
    {
        if (!isManufacturer && companyId == Guid.Empty)
            throw new ArgumentException("Company id is required.", nameof(companyId));
        if (isManufacturer && companyId is not null)
            throw new ArgumentException("Manufacturer accounts cannot belong to a client company.", nameof(companyId));

        var normalizedUsername = Normalize(username);
        var normalizedEmail = Normalize(email);
        if (normalizedUsername.Length == 0 || normalizedUsername.Length > 100 || normalizedUsername.Contains('@'))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (normalizedEmail.Length == 0 || normalizedEmail.Length > 320 || !HasEmailSeparator(normalizedEmail))
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
        CompanyId = companyId;
        IsManufacturer = isManufacturer;
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Username { get; private set; }

    public string UsernameNormalized { get; private set; }

    public string Email { get; private set; }

    public string EmailNormalized { get; private set; }

    public string PasswordHash { get; private set; }

    public Guid? CompanyId { get; private set; }

    public bool IsManufacturer { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void UpdateDetails(string username, string email)
    {
        var normalizedUsername = Normalize(username);
        var normalizedEmail = Normalize(email);
        if (normalizedUsername.Length == 0 || normalizedUsername.Length > 100 || normalizedUsername.Contains('@') ||
            normalizedEmail.Length == 0 || normalizedEmail.Length > 320 || !HasEmailSeparator(normalizedEmail))
        {
            throw new ArgumentException("Username and email are required.");
        }

        Username = normalizedUsername;
        UsernameNormalized = normalizedUsername.ToUpperInvariant();
        Email = normalizedEmail;
        EmailNormalized = normalizedEmail.ToLowerInvariant();
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
    }

    public static UserIdentity Create(string username, string email, string passwordHash) =>
        new(username, email, passwordHash, null, false);

    public static UserIdentity CreateForCompany(Guid companyId, string username, string email, string passwordHash) =>
        new(username, email, passwordHash, companyId, false);

    public static UserIdentity CreateManufacturer(string username, string email, string passwordHash) =>
        new(username, email, passwordHash, null, true);

    private static string Normalize(string value) =>
        (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);

    private static bool HasEmailSeparator(string value)
    {
        var separator = value.IndexOf('@');
        return separator > 0 && separator < value.Length - 1;
    }
}
