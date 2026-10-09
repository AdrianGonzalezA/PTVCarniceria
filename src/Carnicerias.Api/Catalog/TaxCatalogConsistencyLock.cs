using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Catalog;

internal static class TaxCatalogConsistencyLock
{
    public static Task AcquireAsync(PlatformAccessDbContext db, Guid taxId,
        CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(647824, hashtext({taxId.ToString()}))",
            cancellationToken);
}
