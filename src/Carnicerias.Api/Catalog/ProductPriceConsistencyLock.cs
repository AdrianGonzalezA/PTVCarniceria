using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Catalog;

internal static class ProductPriceConsistencyLock
{
    public static Task AcquireAsync(
        PlatformAccessDbContext db, Guid companyId, Guid productId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(647823, hashtext({$"{companyId}:{productId}"}))",
            cancellationToken);
}
