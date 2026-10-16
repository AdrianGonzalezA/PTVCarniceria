using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralManufacturerIdentityTests
{
    [Fact]
    public void ManufacturerHasNoTenantAndUsesIndependentSession()
    {
        var hasher = new CentralPasswordHasher();
        var manufacturer = CentralManufacturer.Create(" superadmin-central ",
            hasher.Hash("a distinct central password"));
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var session = CentralManufacturerSession.Create(manufacturer.Id,
            new string('A', 64), now);

        Assert.Equal("superadmin-central", manufacturer.Username);
        Assert.Equal("SUPERADMIN-CENTRAL", manufacturer.UsernameNormalized);
        Assert.True(session.IsActiveAt(now));
        Assert.False(session.IsActiveAt(now.AddHours(8)));
        session.Revoke(now.AddMinutes(1));
        Assert.False(session.IsActiveAt(now.AddMinutes(2)));
    }

    [Fact]
    public void ManufacturerRejectsWeakIdentityAndNonArgonPasswordHash()
    {
        Assert.Throws<ArgumentException>(() => CentralManufacturer.Create(" ", "$argon2id$hash"));
        Assert.Throws<ArgumentException>(() => CentralManufacturer.Create("admin", "cleartext"));
        Assert.Throws<ArgumentException>(() => CentralManufacturerSession.Create(
            Guid.Empty, new string('A', 64), DateTimeOffset.UtcNow));
    }
}
