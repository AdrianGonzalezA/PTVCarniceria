using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class CloverGatewaySettingsTests
{
    [Fact]
    public void StoresOnlyProtectedCredentialsAndTokenExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.NewGuid();
        var settings = new CloverGatewaySettings(Guid.NewGuid(), "APP_123", "MERCHANT_123", actor, now);

        settings.ReplaceClientSecret([1, 2, 3], actor, now);
        settings.ReplaceTokens([4, 5, 6], [7, 8, 9], now.AddMinutes(30), now.AddDays(30), actor, now);

        Assert.True(settings.HasClientSecret);
        Assert.True(settings.IsConnected);
        Assert.Equal(now.AddMinutes(30), settings.AccessTokenExpiresAtUtc);
        Assert.Equal(now.AddDays(30), settings.RefreshTokenExpiresAtUtc);
        Assert.Equal([4, 5, 6], settings.ProtectedAccessToken);
    }

    [Fact]
    public void ChangingMerchantInvalidatesPreviousSecretAndTokens()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.NewGuid();
        var settings = new CloverGatewaySettings(Guid.NewGuid(), "APP_123", "MERCHANT_123", actor, now);
        settings.ReplaceClientSecret([1], actor, now);
        settings.ReplaceTokens([2], [3], now.AddMinutes(30), now.AddDays(30), actor, now);

        settings.UpdateIdentity("APP_123", "MERCHANT_456", actor, now.AddMinutes(1));

        Assert.False(settings.HasClientSecret);
        Assert.False(settings.IsConnected);
        Assert.Null(settings.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public void RejectsInvalidIdentityAndExpiration()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            new CloverGatewaySettings(Guid.NewGuid(), "../other", "MERCHANT_123", actor, now));

        var settings = new CloverGatewaySettings(Guid.NewGuid(), "APP_123", "MERCHANT_123", actor, now);
        Assert.Throws<ArgumentException>(() =>
            settings.ReplaceTokens([1], [2], now.AddMinutes(-1), now.AddDays(30), actor, now));
    }

    [Fact]
    public void RefreshAttemptBlocksReuseUntilNewPairOrAuthorizationSupersedesIt()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.NewGuid();
        var settings = new CloverGatewaySettings(Guid.NewGuid(), "APP_123", "MERCHANT_123", actor, now);
        settings.ReplaceTokens([1], [2], now.AddMinutes(1), now.AddDays(1), actor, now);
        var attempt = Guid.NewGuid();

        settings.BeginRefresh(attempt, actor, now.AddMinutes(2));

        Assert.Equal(attempt, settings.RefreshAttemptId);
        Assert.Throws<InvalidOperationException>(() =>
            settings.BeginRefresh(Guid.NewGuid(), actor, now.AddMinutes(3)));
        settings.ReplaceTokens([3], [4], now.AddMinutes(35), now.AddDays(2), actor, now.AddMinutes(4));
        Assert.Null(settings.RefreshAttemptId);
    }
}
