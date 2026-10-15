using Carnicerias.Central.Bootstrap;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralManufacturerBootstrapTests
{
    [Fact]
    public void PreparesDistinctManufacturerWithOnlyHashedPassword()
    {
        var hasher = new CentralPasswordHasher();
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        var plan = CentralManufacturerBootstrapService.Prepare(
            "superadmin-central", "a distinct central password", hasher, now);

        Assert.True(hasher.Verify("a distinct central password", plan.Manufacturer.PasswordHash));
        Assert.NotEqual("a distinct central password", plan.Manufacturer.PasswordHash);
        Assert.Equal(CentralAuditAction.ManufacturerCreated, plan.Audit.Action);
        Assert.Null(plan.Audit.CentralCompanyId);
        Assert.Equal(plan.Manufacturer.Id, plan.Audit.ActorId);
    }
}
