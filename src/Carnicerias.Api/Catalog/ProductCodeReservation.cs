using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Catalog;

internal static class ProductCodeReservation
{
    public static Task AcquireCompanyLockAsync(
        PlatformAccessDbContext db, Guid companyId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(647821, hashtext({companyId.ToString()}))",
            cancellationToken);
}
