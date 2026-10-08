using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class CustomerAccountTests
{
    [Fact]
    public void CustomerStartsWithoutCreditPermissionAndCanBeEnabledExplicitly()
    {
        var customer = new CustomerAccount(Guid.NewGuid(), "CLI-001", "Cliente de prueba");

        Assert.True(customer.IsActive);
        Assert.False(customer.CreditEnabled);

        customer.EnableCredit();
        Assert.True(customer.CreditEnabled);
        Assert.True(customer.CanChargeToAccount);

        customer.DisableCredit();
        Assert.False(customer.CreditEnabled);
        Assert.False(customer.CanChargeToAccount);
    }

    [Fact]
    public void CustomerPreservesIdentityWhenEditedOrDisabled()
    {
        var customer = new CustomerAccount(Guid.NewGuid(), " cli-001 ", " Nombre original ");
        var id = customer.Id;

        customer.EnableCredit();
        customer.UpdateDetails(" cli-002 ", " Nuevo nombre ");
        customer.Deactivate();

        Assert.Equal(id, customer.Id);
        Assert.Equal("cli-002", customer.Code);
        Assert.Equal("CLI-002", customer.NormalizedCode);
        Assert.Equal("Nuevo nombre", customer.Name);
        Assert.False(customer.IsActive);
        Assert.False(customer.CanChargeToAccount);

        customer.Activate();
        Assert.True(customer.IsActive);
        Assert.False(customer.CreditEnabled);
    }

    [Theory]
    [InlineData("", "Cliente")]
    [InlineData("CLI-001", " ")]
    [InlineData(" ", "Cliente")]
    public void CustomerRejectsMissingIdentity(string code, string name)
    {
        Assert.Throws<ArgumentException>(() => new CustomerAccount(Guid.NewGuid(), code, name));
    }
}
