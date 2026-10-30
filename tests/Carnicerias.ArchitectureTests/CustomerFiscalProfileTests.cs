using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class CustomerFiscalProfileTests
{
    [Fact]
    public void ProfileIsOptionalAndCanBeUpdatedForOneCompanyCustomer()
    {
        var customer = new CustomerAccount(Guid.NewGuid(), "C-1", "Carnicería Cliente");

        customer.UpdateFiscalProfile(SaleRecipientTaxStatus.Registered,
            "30-00000000-7", "Calle 123", "Córdoba", "Córdoba");

        Assert.Equal(SaleRecipientTaxStatus.Registered, customer.FiscalTaxStatus);
        Assert.Equal("30000000007", customer.FiscalDocumentNumber);
        Assert.Equal("Calle 123", customer.FiscalAddress);
        Assert.Equal("Córdoba", customer.FiscalLocality);
        Assert.Equal("Córdoba", customer.FiscalProvince);
    }

    [Fact]
    public void InvalidOrIncompleteRegisteredProfileIsRejected()
    {
        var customer = new CustomerAccount(Guid.NewGuid(), "C-1", "Carnicería Cliente");

        Assert.Throws<ArgumentException>(() => customer.UpdateFiscalProfile(
            SaleRecipientTaxStatus.Registered, "30000000000", "Calle 123", null, null));
        Assert.Throws<ArgumentException>(() => customer.UpdateFiscalProfile(
            SaleRecipientTaxStatus.Registered, "30000000007", null, null, null));
        Assert.Null(customer.FiscalTaxStatus);
    }
}
