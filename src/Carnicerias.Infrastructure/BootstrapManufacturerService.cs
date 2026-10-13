using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Infrastructure;

public sealed class BootstrapManufacturerService(PlatformAccessDbContext db, IPasswordHasher passwordHasher)
{
    public async Task<Guid> CreateFirstSuperadminAsync(
        string username, string email, string password, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1128352846, 1112493908);",
            cancellationToken);
        if (await db.Users.AnyAsync(user => user.IsManufacturer, cancellationToken))
            throw new InvalidOperationException("La instalación ya tiene un superadmin del fabricante.");

        var user = UserIdentity.CreateManufacturer(username, email, passwordHasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.Id;
    }
}
