using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class SaleCommercialDetailsTests
{
    [Fact]
    public void FiscalRecipientKeepsLocalityAndProvince()
    {
        var choice = SaleDocumentChoice.Create("electronicInvoice", "registered",
            "Cliente SA", "30000000007", "Calle 123", 100m,
            "Córdoba", "Córdoba");

        Assert.Equal("Córdoba", choice.RecipientLocality);
        Assert.Equal("Córdoba", choice.RecipientProvince);
    }

    [Fact]
    public void ConfirmedSaleKeepsOptionalOrderAndFiscalCustomerSnapshot()
    {
        var companyId = Guid.NewGuid();
        var choice = SaleDocumentChoice.Create("electronicInvoice", "registered",
            "Cliente SA", "30000000007", "Calle 123", 100m,
            "Córdoba", "Córdoba");
        var sale = new ConfirmedSale(companyId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, "hash",
            DateTimeOffset.UtcNow, documentChoice: choice, fiscalCustomerId: Guid.NewGuid(),
            fiscalCustomerCode: "CLI-1", purchaseOrderNumber: "OC-42");

        Assert.Equal("CLI-1", sale.FiscalCustomerCode);
        Assert.Equal("OC-42", sale.PurchaseOrderNumber);
        Assert.Equal("Córdoba", sale.RecipientLocality);
    }
}
