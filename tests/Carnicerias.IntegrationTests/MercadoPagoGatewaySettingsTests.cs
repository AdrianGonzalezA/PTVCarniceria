using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class MercadoPagoGatewaySettingsTests
{
    [Fact]
    public void KeepsCredentialsWriteOnlyAndAllowsIndependentModesPerRegister()
    {
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var settings = new MercadoPagoGatewaySettings(company, "123456789", actor, DateTimeOffset.UtcNow);
        settings.ReplaceAccessToken([1, 2, 3], actor, DateTimeOffset.UtcNow);
        settings.ReplaceWebhookSecret([4, 5, 6], actor, DateTimeOffset.UtcNow);
        Assert.True(settings.HasAccessToken);
        Assert.True(settings.HasWebhookSecret);

        var register = new MercadoPagoRegisterSettings(company, Guid.NewGuid(), Guid.NewGuid(),
            "SUC1CAJA1", "NEWLAND_N950__SBX0000001", actor, DateTimeOffset.UtcNow);
        Assert.Equal("SUC1CAJA1", register.QrExternalPosId);
        Assert.Equal("NEWLAND_N950__SBX0000001", register.PointTerminalId);
        register.Update(null, "NEWLAND_N950__SBX0000001", actor, DateTimeOffset.UtcNow);
        Assert.Null(register.QrExternalPosId);
    }

    [Fact]
    public void RejectsInvalidRegisterIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => new MercadoPagoRegisterSettings(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "bad space", null, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
