namespace Carnicerias.Central.Infrastructure;

public sealed class CentralSession
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private CentralSession()
    {
        TokenHash = string.Empty;
    }

    private CentralSession(Guid administratorId, Guid centralCompanyId, string tokenHash, DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        AdministratorId = administratorId;
        CentralCompanyId = centralCompanyId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc.Add(Lifetime);
    }

    public Guid Id { get; private set; }
    public Guid AdministratorId { get; private set; }
    public Guid CentralCompanyId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static CentralSession Create(Guid administratorId, Guid centralCompanyId,
        string? tokenHash, DateTimeOffset createdAtUtc)
    {
        if (administratorId == Guid.Empty || centralCompanyId == Guid.Empty)
            throw new ArgumentException("Administrator and central company are required.");
        if (tokenHash is null || tokenHash.Length != 64 || !tokenHash.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 token hash is required.", nameof(tokenHash));
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Session timestamp must be UTC.", nameof(createdAtUtc));
        return new CentralSession(administratorId, centralCompanyId, tokenHash.ToUpperInvariant(), createdAtUtc);
    }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAtUtc is null && now < ExpiresAtUtc;

    public void Revoke(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Revocation timestamp must be UTC.", nameof(now));
        RevokedAtUtc ??= now;
    }
}
