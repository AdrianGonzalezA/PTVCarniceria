using System.Data;
using System.Text;
using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Central.Bootstrap;

public sealed record CentralManufacturerBootstrapPlan(
    CentralManufacturer Manufacturer, CentralAuditEvent Audit);

public static class CentralManufacturerBootstrapService
{
    public static CentralManufacturerBootstrapPlan Prepare(string username, string password,
        ICentralPasswordHasher hasher, DateTimeOffset now)
    {
        var manufacturer = CentralManufacturer.Create(username, hasher.Hash(password));
        var audit = CentralAuditEvent.Record(CentralAuditAction.ManufacturerCreated,
            CentralAuditResult.Succeeded, null, manufacturer.Id, now);
        return new CentralManufacturerBootstrapPlan(manufacturer, audit);
    }

    public static async Task<Guid> CreateAsync(CentralAccessDbContext database,
        string username, string password, ICentralPasswordHasher hasher,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (await database.Manufacturers.AnyAsync(cancellationToken))
            throw new InvalidOperationException("A central SuperAdmin already exists.");

        var plan = Prepare(username, password, hasher, DateTimeOffset.UtcNow);
        database.Manufacturers.Add(plan.Manufacturer);
        database.AuditEvents.Add(plan.Audit);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new InvalidOperationException("A central SuperAdmin already exists."); }
        catch (PostgresException exception) when (exception.SqlState is
            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation)
        { throw new InvalidOperationException("A central SuperAdmin already exists."); }
        return plan.Manufacturer.Id;
    }

    public static CentralAuditEvent PreparePasswordReset(CentralManufacturer manufacturer,
        IEnumerable<CentralManufacturerSession> sessions, string password,
        ICentralPasswordHasher hasher, DateTimeOffset now)
    {
        manufacturer.SetPasswordHash(hasher.Hash(password));
        foreach (var session in sessions) session.Revoke(now);
        return CentralAuditEvent.Record(CentralAuditAction.ManufacturerPasswordReset,
            CentralAuditResult.Succeeded, null, manufacturer.Id, now);
    }

    public static async Task ResetPasswordAsync(CentralAccessDbContext database,
        string username, string password, ICentralPasswordHasher hasher,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Central SuperAdmin username is required.", nameof(username));
        var normalized = username.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var manufacturer = await database.Manufacturers.SingleOrDefaultAsync(
            item => item.UsernameNormalized == normalized && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Central SuperAdmin not found.");
        var sessions = await database.ManufacturerSessions
            .Where(item => item.ManufacturerId == manufacturer.Id && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        var audit = PreparePasswordReset(manufacturer, sessions, password, hasher,
            DateTimeOffset.UtcNow);
        database.AuditEvents.Add(audit);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
