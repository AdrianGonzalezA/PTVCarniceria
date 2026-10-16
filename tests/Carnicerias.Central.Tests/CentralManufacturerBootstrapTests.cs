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

    [Fact]
    public void PasswordRecoveryReplacesHashAndRevokesExistingSessions()
    {
        var hasher = new CentralPasswordHasher();
        var manufacturer = CentralManufacturer.Create("superadmin-central",
            hasher.Hash("original distinct central password"));
        var issuedAt = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var session = CentralManufacturerSession.Create(manufacturer.Id, new string('A', 64), issuedAt);
        var recoveredAt = issuedAt.AddMinutes(30);

        var audit = CentralManufacturerBootstrapService.PreparePasswordReset(manufacturer,
            [session], "replacement distinct central password", hasher, recoveredAt);

        Assert.True(hasher.Verify("replacement distinct central password", manufacturer.PasswordHash));
        Assert.False(hasher.Verify("original distinct central password", manufacturer.PasswordHash));
        Assert.False(session.IsActiveAt(recoveredAt));
        Assert.Equal(CentralAuditAction.ManufacturerPasswordReset, audit.Action);
        Assert.Equal(manufacturer.Id, audit.ActorId);
        Assert.Null(audit.CentralCompanyId);
    }
}
