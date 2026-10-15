using System.Security.Cryptography;
using System.Text;

namespace Carnicerias.Central.Infrastructure;

public sealed record CentralManufacturerSessionContext(
    CentralManufacturerSession Session, CentralManufacturer Manufacturer);
public sealed record CentralManufacturerSessionIssue(
    string Token, CentralManufacturerSession Session, CentralManufacturer Manufacturer);

public interface ICentralManufacturerSessionStore
{
    Task<CentralManufacturer?> FindAccountAsync(string usernameNormalized,
        CancellationToken cancellationToken);
    Task RecordDeniedLoginAsync(CancellationToken cancellationToken);
    Task SaveIssuedSessionAsync(CentralManufacturerSession session,
        CancellationToken cancellationToken);
    Task<CentralManufacturerSessionContext?> FindSessionAsync(string tokenHash,
        CancellationToken cancellationToken);
    Task SaveRevokedSessionAsync(CentralManufacturerSession session,
        CancellationToken cancellationToken);
}

public sealed class CentralManufacturerSessionService(
    ICentralManufacturerSessionStore store, ICentralPasswordHasher passwordHasher,
    TimeProvider clock)
{
    private static readonly Lazy<string> DecoyHash = new(() =>
        new CentralPasswordHasher().Hash("central manufacturer unknown account timing decoy"));

    public async Task<CentralManufacturerSessionIssue?> LoginAsync(
        string? username, string? password, CancellationToken cancellationToken)
    {
        var normalized = (username ?? string.Empty).Trim()
            .Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        var account = await store.FindAccountAsync(normalized, cancellationToken);
        var verified = passwordHasher.Verify(password, account?.PasswordHash ?? DecoyHash.Value);
        if (!verified || account is null || !account.IsActive)
        {
            await store.RecordDeniedLoginAsync(cancellationToken);
            return null;
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token;
        try { token = Convert.ToHexString(tokenBytes); }
        finally { CryptographicOperations.ZeroMemory(tokenBytes); }

        var session = CentralManufacturerSession.Create(account.Id,
            HashToken(token), clock.GetUtcNow());
        await store.SaveIssuedSessionAsync(session, cancellationToken);
        return new CentralManufacturerSessionIssue(token, session, account);
    }

    public async Task<CentralManufacturerSessionContext?> AuthenticateAsync(
        string? token, CancellationToken cancellationToken)
    {
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var context = await store.FindSessionAsync(HashToken(token), cancellationToken);
        return context is not null && context.Manufacturer.IsActive
            && context.Session.ManufacturerId == context.Manufacturer.Id
            && context.Session.IsActiveAt(clock.GetUtcNow()) ? context : null;
    }

    public async Task<bool> LogoutAsync(string? token, CancellationToken cancellationToken)
    {
        var context = await AuthenticateAsync(token, cancellationToken);
        if (context is null) return false;
        context.Session.Revoke(clock.GetUtcNow());
        await store.SaveRevokedSessionAsync(context.Session, cancellationToken);
        return true;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
