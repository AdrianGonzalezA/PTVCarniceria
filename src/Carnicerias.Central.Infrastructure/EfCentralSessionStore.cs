using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Infrastructure;

public sealed class EfCentralSessionStore(CentralAccessDbContext database) : ICentralSessionStore
{
    public Task<CentralAccount?> FindAccountAsync(
        string companyCode,
        string usernameNormalized,
        CancellationToken cancellationToken) =>
        (from company in database.Companies
         join administrator in database.Administrators
             on company.Id equals administrator.CentralCompanyId
         where company.Code == companyCode
             && administrator.UsernameNormalized == usernameNormalized
         select new CentralAccount(company, administrator))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task RecordDeniedLoginAsync(Guid? companyId, CancellationToken cancellationToken)
    {
        database.AuditEvents.Add(CentralAuditEvent.Record(
            CentralAuditAction.SessionDenied, CentralAuditResult.Denied,
            companyId, null, DateTimeOffset.UtcNow));
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveIssuedSessionAsync(CentralSession session, CancellationToken cancellationToken)
    {
        database.Sessions.Add(session);
        database.AuditEvents.Add(CentralAuditEvent.Record(
            CentralAuditAction.SessionIssued, CentralAuditResult.Succeeded,
            session.CentralCompanyId, session.AdministratorId, DateTimeOffset.UtcNow));
        await database.SaveChangesAsync(cancellationToken);
    }

    public Task<CentralSessionContext?> FindSessionAsync(
        string tokenHash,
        CancellationToken cancellationToken) =>
        (from session in database.Sessions
         join administrator in database.Administrators
             on new { session.CentralCompanyId, Id = session.AdministratorId }
             equals new { administrator.CentralCompanyId, administrator.Id }
         join company in database.Companies on session.CentralCompanyId equals company.Id
         where session.TokenHash == tokenHash
         select new CentralSessionContext(session, company, administrator))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task SaveRevokedSessionAsync(CentralSession session, CancellationToken cancellationToken)
    {
        database.AuditEvents.Add(CentralAuditEvent.Record(
            CentralAuditAction.SessionRevoked, CentralAuditResult.Succeeded,
            session.CentralCompanyId, session.AdministratorId, DateTimeOffset.UtcNow));
        await database.SaveChangesAsync(cancellationToken);
    }
}
