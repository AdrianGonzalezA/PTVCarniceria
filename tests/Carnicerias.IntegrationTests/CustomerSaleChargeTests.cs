using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class CustomerSaleChargeTests
{
    [Fact]
    public void CapturesSaleCustomerCashierAndAmountWithoutCreatingACashPayment()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var charge = new CustomerSaleCharge(companyId, branchId, customerId, saleId,
            cashierId, shiftId, 475.25m, now);

        Assert.Equal(companyId, charge.CompanyId);
        Assert.Equal(branchId, charge.BranchId);
        Assert.Equal(customerId, charge.CustomerId);
        Assert.Equal(saleId, charge.SaleId);
        Assert.Equal(cashierId, charge.CashierId);
        Assert.Equal(shiftId, charge.CashierShiftId);
        Assert.Equal(475.25m, charge.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public void RejectsInvalidMonetaryAmount(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CustomerSaleCharge(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), amount, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ConfirmedSaleSnapshotsCustomerIdentityForAChargedTicket()
    {
        var customerId = Guid.NewGuid();
        var sale = new ConfirmedSale(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2500m, "request-hash",
            DateTimeOffset.UtcNow, customerId: customerId, accountChargeAmount: 1000m,
            customerCode: "CLI-CC-PRUEBA", customerName: "Cliente prueba cuenta corriente");

        Assert.Equal(customerId, sale.CustomerId);
        Assert.Equal(1000m, sale.AccountChargeAmount);
        Assert.Equal("CLI-CC-PRUEBA", sale.CustomerCode);
        Assert.Equal("Cliente prueba cuenta corriente", sale.CustomerName);
    }
}
