using System.Security.Cryptography;

namespace Carnicerias.PlatformAccess;

public sealed class BranchCentralConnection
{
    private BranchCentralConnection() => DestinationUrl = string.Empty;

    private BranchCentralConnection(Guid companyId, Guid branchId, string destinationUrl,
        Guid actorId, DateTimeOffset now)
    {
        CompanyId = companyId;
        BranchId = branchId;
        InstallationId = Guid.NewGuid();
        DestinationUrl = destinationUrl;
        UpdatedByUserId = actorId;
        UpdatedAtUtc = now;
    }

    public Guid BranchId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InstallationId { get; private set; }
    public string DestinationUrl { get; private set; }
    public bool IsActive { get; private set; }
    public string? CredentialSha256 { get; private set; }
    public string? PreviousCredentialSha256 { get; private set; }
    public DateTimeOffset? PreviousCredentialExpiresAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public static BranchCentralConnection Create(Guid companyId, Guid branchId,
        string? destinationUrl, bool allowLabLoopback, Guid actorId, DateTimeOffset now)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("Company, branch and actor are required.");
        RequireUtc(now);
        return new BranchCentralConnection(companyId, branchId,
            NormalizeUrl(destinationUrl, allowLabLoopback), actorId, now);
    }

    public void UpdateUrl(string? destinationUrl, bool allowLabLoopback,
        Guid actorId, DateTimeOffset now)
    {
        RequireActorAndUtc(actorId, now);
        var normalized = NormalizeUrl(destinationUrl, allowLabLoopback);
        DestinationUrl = normalized;
        Touch(actorId, now);
    }

    public void IssueCredential(string token, DateTimeOffset now, Guid? actorId = null)
    {
        RequireActorAndUtc(actorId ?? UpdatedByUserId, now);
        var hash = HashToken(token);
        if (IsActive && PreviousCredentialSha256 is not null &&
            PreviousCredentialExpiresAtUtc > now)
            throw new InvalidOperationException("Finish the previous rotation first.");
        PreviousCredentialSha256 = IsActive ? CredentialSha256 : null;
        PreviousCredentialExpiresAtUtc = IsActive ? now.AddHours(1) : null;
        CredentialSha256 = hash;
        IsActive = true;
        Touch(actorId ?? UpdatedByUserId, now);
    }

    public void CompleteRotation(DateTimeOffset now, Guid? actorId = null)
    {
        RequireActorAndUtc(actorId ?? UpdatedByUserId, now);
        PreviousCredentialSha256 = null;
        PreviousCredentialExpiresAtUtc = null;
        Touch(actorId ?? UpdatedByUserId, now);
    }

    public void Deactivate(DateTimeOffset now, Guid? actorId = null)
    {
        RequireActorAndUtc(actorId ?? UpdatedByUserId, now);
        IsActive = false;
        CredentialSha256 = null;
        PreviousCredentialSha256 = null;
        PreviousCredentialExpiresAtUtc = null;
        Touch(actorId ?? UpdatedByUserId, now);
    }

    public bool VerifyCredential(string? token, DateTimeOffset now)
    {
        if (!IsActive || token is null || token.Length != 64) return false;
        string hash;
        try { hash = HashToken(token); }
        catch (ArgumentException) { return false; }
        var candidate = Convert.FromHexString(hash);
        return Matches(candidate, CredentialSha256) ||
            PreviousCredentialExpiresAtUtc > now &&
            Matches(candidate, PreviousCredentialSha256);
    }

    private static bool Matches(byte[] candidate, string? storedHash)
    {
        if (storedHash is not { Length: 64 }) return false;
        try { return CryptographicOperations.FixedTimeEquals(candidate, Convert.FromHexString(storedHash)); }
        catch (FormatException) { return false; }
    }

    private static string HashToken(string token)
    {
        if (token.Length != 64) throw new ArgumentException("Invalid token length.", nameof(token));
        try { return Convert.ToHexString(SHA256.HashData(Convert.FromHexString(token))); }
        catch (FormatException) { throw new ArgumentException("Invalid token format.", nameof(token)); }
    }

    private static string NormalizeUrl(string? value, bool allowLabLoopback)
    {
        if (value is null || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttps &&
             !(allowLabLoopback && url.Scheme == Uri.UriSchemeHttp &&
               url.Host is "127.0.0.1" or "[::1]")) ||
            url.UserInfo.Length > 0 || url.Query.Length > 0 ||
            url.Fragment.Length > 0 || url.AbsolutePath != "/")
            throw new ArgumentException("Invalid branch destination URL.", nameof(value));
        return url.AbsoluteUri;
    }

    private void Touch(Guid actorId, DateTimeOffset now)
    {
        UpdatedByUserId = actorId;
        UpdatedAtUtc = now;
        Revision++;
    }

    private static void RequireUtc(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(now));
    }

    private static void RequireActorAndUtc(Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        RequireUtc(now);
    }
}
