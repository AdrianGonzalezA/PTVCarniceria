using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.ArchitectureTests;

public sealed class PaymentDetailCatalogTests
{
    [Fact]
    public void ModelsSeparateConfigurationTablesForCardBrandsProcessorsBanksAndWallets()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused")
            .Options;
        using var db = new PlatformAccessDbContext(options);

        Assert.Equal("card_brands", db.Model.FindEntityType(typeof(PaymentCardBrand))!.GetTableName());
        Assert.Equal("payment_processors", db.Model.FindEntityType(typeof(PaymentProcessor))!.GetTableName());
        Assert.Equal("financial_institutions", db.Model.FindEntityType(typeof(FinancialInstitution))!.GetTableName());
        Assert.Equal("digital_wallets", db.Model.FindEntityType(typeof(PaymentWallet))!.GetTableName());
        foreach (var type in new[] { typeof(PaymentCardBrand), typeof(PaymentProcessor),
                     typeof(FinancialInstitution), typeof(PaymentWallet) })
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.Equal("payment_config", entity.GetSchema());
            Assert.Equal("Code", Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        }
    }

    [Fact]
    public void CardBrandDeclaresItsSupportedPaymentTypes()
    {
        var debit = new PaymentCardBrand("maestro", "Maestro", true, false);
        var credit = new PaymentCardBrand("amex", "American Express", false, true);
        Assert.True(debit.SupportsDebit);
        Assert.False(debit.SupportsCredit);
        Assert.False(credit.SupportsDebit);
        Assert.True(credit.SupportsCredit);
    }
}
