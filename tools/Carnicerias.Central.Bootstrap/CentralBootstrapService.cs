using System.Text;
using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Central.Bootstrap;

public sealed record CentralCompanyBootstrapPlan(
    CentralCompany Company,
    CentralAdministrator Administrator,
    IReadOnlyList<CentralAuditEvent> AuditEvents);

public sealed class CentralBootstrapService(
    CentralAccessDbContext database,
    ICentralPasswordHasher passwordHasher)
{
    public static CentralCompanyBootstrapPlan PrepareCompany(
        string? companyCode,
        string? companyName,
        string? username,
        string password,
        ICentralPasswordHasher passwordHasher,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(passwordHasher);
        var company = CentralCompany.Create(companyCode, companyName);
        var administrator = CentralAdministrator.Create(
            company.Id, username, passwordHasher.Hash(password));
        var auditEvents = new[]
        {
            CentralAuditEvent.Record(CentralAuditAction.CompanyCreated,
                CentralAuditResult.Succeeded, company.Id, null, now),
            CentralAuditEvent.Record(CentralAuditAction.AdministratorCreated,
                CentralAuditResult.Succeeded, company.Id, null, now)
        };
        return new CentralCompanyBootstrapPlan(company, administrator, auditEvents);
    }

    public async Task<Guid> CreateCompanyAsync(
        string? companyCode,
        string? companyName,
        string? username,
        string password,
        CancellationToken cancellationToken)
    {
        var plan = PrepareCompany(companyCode, companyName, username,
            password, passwordHasher, DateTimeOffset.UtcNow);
        if (await database.Companies.AnyAsync(
                company => company.Code == plan.Company.Code, cancellationToken))
        {
            throw new InvalidOperationException("Central company code already exists.");
        }

        database.Companies.Add(plan.Company);
        database.Administrators.Add(plan.Administrator);
        database.AuditEvents.AddRange(plan.AuditEvents);
        // A single SaveChanges is atomic for relational providers.
        // https://learn.microsoft.com/en-us/ef/core/saving/basic#multiple-operations-in-a-single-savechanges
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                               { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new InvalidOperationException("Central company or administrator already exists.");
        }

        return plan.Company.Id;
    }

    public async Task ResetPasswordAsync(
        string? companyCode,
        string? username,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var normalizedCode = (companyCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedUsername = (username ?? string.Empty).Trim()
            .Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        if (normalizedCode.Length is < 2 or > 40 || normalizedUsername.Length is < 1 or > 100)
        {
            throw new ArgumentException("Invalid central account selector.");
        }

        var administrator = await (
            from company in database.Companies
            join account in database.Administrators on company.Id equals account.CentralCompanyId
            where company.Code == normalizedCode && account.UsernameNormalized == normalizedUsername
            select account).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Central account not found.");
        var sessions = await database.Sessions
            .Where(session => session.CentralCompanyId == administrator.CentralCompanyId
                && session.AdministratorId == administrator.Id
                && session.RevokedAtUtc == null)
            .ToArrayAsync(cancellationToken);
        var audit = ApplyPasswordReset(administrator, sessions, newPassword,
            passwordHasher, DateTimeOffset.UtcNow);
        database.AuditEvents.Add(audit);
        await database.SaveChangesAsync(cancellationToken);
    }

    public static CentralAuditEvent ApplyPasswordReset(
        CentralAdministrator administrator,
        IEnumerable<CentralSession> sessions,
        string newPassword,
        ICentralPasswordHasher passwordHasher,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        var affectedSessions = sessions.ToArray();
        if (affectedSessions.Any(session => session.CentralCompanyId != administrator.CentralCompanyId
            || session.AdministratorId != administrator.Id))
        {
            throw new InvalidOperationException("Password reset includes an unrelated central session.");
        }

        var hash = passwordHasher.Hash(newPassword);
        administrator.SetPasswordHash(hash);
        foreach (var session in affectedSessions)
        {
            session.Revoke(now);
        }

        return CentralAuditEvent.Record(CentralAuditAction.PasswordReset,
            CentralAuditResult.Succeeded, administrator.CentralCompanyId, null, now);
    }
}
