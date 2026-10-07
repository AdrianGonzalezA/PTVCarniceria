using System.Security.Cryptography;
using System.Text;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Infrastructure;

public sealed record CreatedUserSession(string Credential, UserIdentity User, UserSession Session);

public sealed record AuthenticatedUserSession(UserIdentity User, UserSession Session);

public sealed class SessionAuthenticationService(
    PlatformAccessDbContext db,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider)
{
    private static readonly Lazy<string> DummyPasswordHash = new(
        () => new Argon2idPasswordHasher().Hash("not-a-real-user-password"));

    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);

    public async Task<CreatedUserSession?> CreateAsync(
        string credential,
        string password,
        Guid? terminalId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCredential = Normalize(credential);
        var normalizedUsername = normalizedCredential.ToUpperInvariant();
        var normalizedEmail = normalizedCredential.ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.UsernameNormalized == normalizedUsername ||
                         candidate.EmailNormalized == normalizedEmail,
            cancellationToken);

        var passwordMatches = passwordHasher.Verify(password, user?.PasswordHash ?? DummyPasswordHash.Value);
        if (!passwordMatches || user is not { IsActive: true })
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var credentialBytes = RandomNumberGenerator.GetBytes(32);
        var sessionCredential = Convert.ToBase64String(credentialBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        CryptographicOperations.ZeroMemory(credentialBytes);

        var tokenHash = HashCredential(sessionCredential);
        var session = new UserSession(user.Id, tokenHash, now, now.Add(AbsoluteLifetime));
        if (terminalId is Guid boundTerminalId)
            session.BindToTerminal(boundTerminalId);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatedUserSession(sessionCredential, user, session);
    }

    public async Task<AuthenticatedUserSession?> FindActiveAsync(
        string? credential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 128)
        {
            return null;
        }

        var tokenHash = HashCredential(credential);
        var session = await db.Sessions.SingleOrDefaultAsync(
            candidate => candidate.TokenHash == tokenHash,
            cancellationToken);
        if (session is null)
        {
            return null;
        }

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == session.UserId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (user is not { IsActive: true } || !session.IsActiveAt(now))
        {
            session.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        session.Touch(now);
        await db.SaveChangesAsync(cancellationToken);
        return new AuthenticatedUserSession(user, session);
    }

    public async Task RevokeAsync(string? credential, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 128)
        {
            return;
        }

        var tokenHash = HashCredential(credential);
        var session = await db.Sessions.SingleOrDefaultAsync(
            candidate => candidate.TokenHash == tokenHash,
            cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormKC);

    private static string HashCredential(string credential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential))).ToLowerInvariant();
}
