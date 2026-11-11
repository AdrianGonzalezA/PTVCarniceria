using Carnicerias.Api.Customers;

namespace Carnicerias.IntegrationTests;

public sealed class FiscalRegistryMockTests
{
    [Fact]
    public void SimulatedRegistryProfileIsClearlyLabeledAndRegistered()
    {
        var profile = PosCustomerEndpoints.SimulatedRegisteredLookup(null, "20164755100");

        Assert.Equal("simulated", profile.Source);
        Assert.Equal("20164755100", profile.DocumentNumber);
        Assert.Equal("registered", profile.TaxStatus);
        Assert.Equal("RESPONSABLE INSCRIPTO DE PRUEBA", profile.Name);
        Assert.Equal("DOMICILIO DE PRUEBA", profile.Address);
        Assert.Contains("simulados", profile.Notice!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(profile.CustomerId);
    }
}
