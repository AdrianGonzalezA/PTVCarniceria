using System.Data;
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
}
