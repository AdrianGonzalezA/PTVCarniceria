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
        Guid companyId,
        string credential,
        string password,
        Guid? terminalId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCredential = Normalize(credential);
        var normalizedUsername = normalizedCredential.ToUpperInvariant();
        var normalizedEmail = normalizedCredential.ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.CompanyId == companyId && !candidate.IsManufacturer && candidate.IsActive &&
                         (candidate.UsernameNormalized == normalizedUsername ||
                          candidate.EmailNormalized == normalizedEmail),
            cancellationToken);

        var passwordMatches = passwordHasher.Verify(password, user?.PasswordHash ?? DummyPasswordHash.Value);
        var companyActive = user is not null && await db.Companies.AnyAsync(
            company => company.Id == companyId && company.IsActive, cancellationToken);
        if (!passwordMatches || !companyActive || user is null)
        {
            return null;
        }

        return await IssueAsync(user, companyId, terminalId, cancellationToken);
    }

    public async Task<CreatedUserSession?> CreateManufacturerAsync(
        string credential,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedCredential = Normalize(credential);
        var normalizedUsername = normalizedCredential.ToUpperInvariant();
        var normalizedEmail = normalizedCredential.ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.IsManufacturer &&
            candidate.CompanyId == null && candidate.IsActive &&
            (candidate.UsernameNormalized == normalizedUsername || candidate.EmailNormalized == normalizedEmail),
            cancellationToken);
        var passwordMatches = passwordHasher.Verify(password, user?.PasswordHash ?? DummyPasswordHash.Value);
        return passwordMatches && user is not null
            ? await IssueAsync(user, null, null, cancellationToken)
            : null;
    }

    private async Task<CreatedUserSession> IssueAsync(
        UserIdentity user, Guid? companyId, Guid? terminalId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var credentialBytes = RandomNumberGenerator.GetBytes(32);
        var sessionCredential = Convert.ToBase64String(credentialBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        CryptographicOperations.ZeroMemory(credentialBytes);

        var tokenHash = HashCredential(sessionCredential);
        var session = new UserSession(user.Id, tokenHash, now, now.Add(AbsoluteLifetime));
        if (companyId is Guid boundCompanyId)
            session.BindToCompany(boundCompanyId);
        if (terminalId is Guid boundTerminalId)
            session.BindToTerminal(boundTerminalId);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatedUserSession(sessionCredential, user, session);
    }

    public async Task<AuthenticatedUserSession?> FindManufacturerActiveAsync(
        string? credential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 128)
            return null;

        var tokenHash = HashCredential(credential);
        var session = await db.Sessions.SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash,
            cancellationToken);
        if (session is null)
            return null;

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == session.UserId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (user is not { IsActive: true, IsManufacturer: true, CompanyId: null } ||
            session.CompanyId is not null || session.BranchId is not null ||
            session.PosTerminalId is not null || !session.IsActiveAt(now))
        {
            session.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        session.Touch(now);
        await db.SaveChangesAsync(cancellationToken);
        return new AuthenticatedUserSession(user, session);
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
        if (user is not { IsActive: true, IsManufacturer: false } ||
            user.CompanyId is null || user.CompanyId != session.CompanyId ||
            !session.IsActiveAt(now))
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
