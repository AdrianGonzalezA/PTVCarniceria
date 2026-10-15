namespace Carnicerias.Central.Infrastructure;

public sealed class CentralManufacturerSession
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private CentralManufacturerSession()
    {
        TokenHash = string.Empty;
    }

    private CentralManufacturerSession(Guid manufacturerId, string tokenHash,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        ManufacturerId = manufacturerId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc.Add(Lifetime);
    }

    public Guid Id { get; private set; }
    public Guid ManufacturerId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static CentralManufacturerSession Create(Guid manufacturerId, string? tokenHash,
        DateTimeOffset createdAtUtc)
    {
        if (manufacturerId == Guid.Empty)
            throw new ArgumentException("Central manufacturer is required.", nameof(manufacturerId));
        if (tokenHash is null || tokenHash.Length != 64 || !tokenHash.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 token hash is required.", nameof(tokenHash));
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Session timestamp must be UTC.", nameof(createdAtUtc));
        return new CentralManufacturerSession(manufacturerId, tokenHash.ToUpperInvariant(), createdAtUtc);
    }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAtUtc is null && now < ExpiresAtUtc;

    public void Revoke(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Revocation timestamp must be UTC.", nameof(now));
        RevokedAtUtc ??= now;
    }
}
