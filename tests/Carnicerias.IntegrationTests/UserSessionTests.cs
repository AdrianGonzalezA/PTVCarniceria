using Carnicerias.PlatformAccess;

namespace Carnicerias.IntegrationTests;

public sealed class UserSessionTests
{
    [Fact]
    public void SessionIsActiveBeforeExpiryAndInactiveAfterRevocationOrExpiry()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var session = new UserSession(Guid.NewGuid(), new string('a', 64), now, now.AddHours(8));

        Assert.True(session.IsActiveAt(now.AddMinutes(1)));
        Assert.False(session.IsActiveAt(now.AddHours(8)));

        session.Revoke(now.AddMinutes(2));

        Assert.False(session.IsActiveAt(now.AddMinutes(3)));
        Assert.Equal(now.AddMinutes(2), session.RevokedAtUtc);
    }
}
