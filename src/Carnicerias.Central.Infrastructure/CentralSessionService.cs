using System.Security.Cryptography;
using System.Text;

namespace Carnicerias.Central.Infrastructure;

public sealed record CentralAccount(CentralCompany Company, CentralAdministrator Administrator);
public sealed record CentralSessionContext(
    CentralSession Session, CentralCompany Company, CentralAdministrator Administrator);

public sealed class CentralSessionIssue(
    string token, CentralSession session, CentralCompany company, CentralAdministrator administrator)
{
    public string Token { get; } = token;
    public CentralSession Session { get; } = session;
    public CentralCompany Company { get; } = company;
    public CentralAdministrator Administrator { get; } = administrator;
}

public interface ICentralSessionStore
{
    Task<CentralAccount?> FindAccountAsync(string companyCode, string usernameNormalized,
        CancellationToken cancellationToken);
    Task RecordDeniedLoginAsync(Guid? companyId, CancellationToken cancellationToken);
    Task SaveIssuedSessionAsync(CentralSession session, CancellationToken cancellationToken);
    Task<CentralSessionContext?> FindSessionAsync(string tokenHash, CancellationToken cancellationToken);
    Task SaveRevokedSessionAsync(CentralSession session, CancellationToken cancellationToken);
}

public sealed class CentralSessionService(
    ICentralSessionStore store,
    ICentralPasswordHasher passwordHasher,
    TimeProvider clock)
{
    private static readonly Lazy<string> DecoyHash = new(() =>
        new CentralPasswordHasher().Hash("central unknown account timing decoy"));

    public async Task<CentralSessionIssue?> LoginAsync(
        string? companyCode,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var normalizedCode = (companyCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedUsername = (username ?? string.Empty).Trim()
            .Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        var account = await store.FindAccountAsync(normalizedCode, normalizedUsername, cancellationToken);

        // Missing accounts still pay the Argon2id verification cost.
        var verified = passwordHasher.Verify(password, account?.Administrator.PasswordHash ?? DecoyHash.Value);
        if (!verified || account is null || !account.Company.IsActive || !account.Administrator.IsActive)
        {
            await store.RecordDeniedLoginAsync(account?.Company.Id, cancellationToken);
            return null;
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token;
        try
        {
            token = Convert.ToHexString(tokenBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }

        var session = CentralSession.Create(account.Administrator.Id, account.Company.Id,
            HashToken(token), clock.GetUtcNow());
        await store.SaveIssuedSessionAsync(session, cancellationToken);
        return new CentralSessionIssue(token, session, account.Company, account.Administrator);
    }

    public async Task<CentralSessionContext?> AuthenticateAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            return null;
        }

        var context = await store.FindSessionAsync(HashToken(token), cancellationToken);
        return context is not null
            && context.Session.IsActiveAt(clock.GetUtcNow())
            && context.Company.IsActive
            && context.Administrator.IsActive
            && context.Session.CentralCompanyId == context.Company.Id
            && context.Session.AdministratorId == context.Administrator.Id
            && context.Administrator.CentralCompanyId == context.Company.Id
            ? context : null;
    }

    public async Task<bool> LogoutAsync(string? token, CancellationToken cancellationToken)
    {
        var context = await AuthenticateAsync(token, cancellationToken);
        if (context is null)
        {
            return false;
        }

        context.Session.Revoke(clock.GetUtcNow());
        await store.SaveRevokedSessionAsync(context.Session, cancellationToken);
        return true;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
