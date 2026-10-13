using Carnicerias.Api.Payments;
using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class IntegratedGatewayAvailabilityTests
{
    [Fact]
    public void ShowsOnlyConfiguredMercadoPagoModesForThisRegister()
    {
        var now = DateTimeOffset.UtcNow;
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var seller = new MercadoPagoGatewaySettings(company, "123456", actor, now);
        seller.ReplaceAccessToken([1, 2, 3], actor, now);
        var register = new MercadoPagoRegisterSettings(company, Guid.NewGuid(), Guid.NewGuid(),
            "QR_1", null, actor, now);

        var available = IntegratedGatewayAvailability.ForRegister(seller, register);

        Assert.Single(available);
        Assert.Equal("mercadoPago", available[0].Provider);
        Assert.Equal(["qr"], available[0].Modes);
    }

    [Fact]
    public void DoesNotOfferProviderWithoutSellerTokenOrRegisterMapping()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.NewGuid();
        var seller = new MercadoPagoGatewaySettings(Guid.NewGuid(), "123456", actor, now);
        var register = new MercadoPagoRegisterSettings(seller.CompanyId, Guid.NewGuid(), Guid.NewGuid(),
            "QR_1", "POINT_1", actor, now);

        Assert.Empty(IntegratedGatewayAvailability.ForRegister(seller, register));
        seller.ReplaceAccessToken([1], actor, now);
        Assert.Empty(IntegratedGatewayAvailability.ForRegister(seller, null));
        var anotherCompany = new MercadoPagoGatewaySettings(Guid.NewGuid(), "789", actor, now);
        anotherCompany.ReplaceAccessToken([1], actor, now);
        Assert.Empty(IntegratedGatewayAvailability.ForRegister(anotherCompany, register));
    }
}
