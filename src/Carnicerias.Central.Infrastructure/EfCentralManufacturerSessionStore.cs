using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Infrastructure;

public sealed class EfCentralManufacturerSessionStore(CentralAccessDbContext database)
    : ICentralManufacturerSessionStore
{
    public Task<CentralManufacturer?> FindAccountAsync(string usernameNormalized,
        CancellationToken cancellationToken) => database.Manufacturers.SingleOrDefaultAsync(
            manufacturer => manufacturer.UsernameNormalized == usernameNormalized,
            cancellationToken);

    public async Task RecordDeniedLoginAsync(CancellationToken cancellationToken)
    {
        database.AuditEvents.Add(CentralAuditEvent.Record(
            CentralAuditAction.ManufacturerSessionDenied, CentralAuditResult.Denied,
            null, null, DateTimeOffset.UtcNow));
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveIssuedSessionAsync(CentralManufacturerSession session,
        CancellationToken cancellationToken)
    {
        database.ManufacturerSessions.Add(session);
        database.AuditEvents.Add(CentralAuditEvent.Record(
            CentralAuditAction.ManufacturerSessionIssued, CentralAuditResult.Succeeded,
            null, session.ManufacturerId, DateTimeOffset.UtcNow));
        await database.SaveChangesAsync(cancellationToken);
    }

    public Task<CentralManufacturerSessionContext?> FindSessionAsync(string tokenHash,
        CancellationToken cancellationToken) =>
        (from session in database.ManufacturerSessions
         join manufacturer in database.Manufacturers
             on session.ManufacturerId equals manufacturer.Id
         where session.TokenHash == tokenHash
         select new CentralManufacturerSessionContext(session, manufacturer))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task SaveRevokedSessionAsync(CentralManufacturerSession session,
        CancellationToken cancellationToken)
    {
        database.AuditEvents.Add(CentralAuditEvent.Record(
            CentralAuditAction.ManufacturerSessionRevoked, CentralAuditResult.Succeeded,
            null, session.ManufacturerId, DateTimeOffset.UtcNow));
        await database.SaveChangesAsync(cancellationToken);
    }
}
