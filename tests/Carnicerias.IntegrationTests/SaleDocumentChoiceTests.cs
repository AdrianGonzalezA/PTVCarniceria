using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class SaleDocumentChoiceTests
{
    [Fact]
    public void InternalTicketNeedsNoFiscalRecipient()
    {
        var choice = SaleDocumentChoice.Create("nonFiscalTicket", "finalConsumer", null, null, null, 2500m);

        Assert.Equal(SaleDocumentType.NonFiscalTicket, choice.Type);
        Assert.Null(choice.RecipientDocumentNumber);
    }

    [Fact]
    public void IdentifiedFiscalRecipientNeedsNameCuitAndAddress()
    {
        const string type = "electronicInvoice";
        Assert.Throws<ArgumentException>(() => SaleDocumentChoice.Create(
            type, "registered", "Cliente", null, "Calle 123", 2500m));
        Assert.Throws<ArgumentException>(() => SaleDocumentChoice.Create(
            type, "registered", "Cliente", "20000000001", null, 2500m));

        var choice = SaleDocumentChoice.Create(type, "registered", " Cliente ", "20-00000000-1",
            " Calle 123 ", 2500m);
        Assert.Equal("Cliente", choice.RecipientName);
        Assert.Equal("20000000001", choice.RecipientDocumentNumber);
        Assert.Equal("Calle 123", choice.RecipientAddress);
    }

    [Theory]
    [InlineData("registered")]
    [InlineData("smallTaxpayer")]
    [InlineData("exempt")]
    public void FiscalTicketRejectsRecipientsUnsupportedByCurrentHasarIntegration(string status)
    {
        Assert.Throws<ArgumentException>(() => SaleDocumentChoice.Create(
            "fiscalTicket", status, "Cliente", "20-00000000-1", "Calle 123", 2500m));
    }

    [Fact]
    public void FiscalTicketBRemainsAvailableForFinalConsumer()
    {
        var choice = SaleDocumentChoice.Create("fiscalTicket", "finalConsumer", null, null, null, 2500m);

        Assert.Equal(SaleDocumentType.FiscalTicket, choice.Type);
    }

    [Fact]
    public void LargeFinalConsumerFiscalSaleNeedsIdentification()
    {
        Assert.Throws<ArgumentException>(() => SaleDocumentChoice.Create(
            "electronicInvoice", "finalConsumer", null, null, null, 10_000_000m));

        var choice = SaleDocumentChoice.Create("electronicInvoice", "finalConsumer", null,
            "12345678", null, 10_000_000m);
        Assert.Equal("12345678", choice.RecipientDocumentNumber);
    }

    [Fact]
    public void SmallFinalConsumerFiscalSaleCanRemainAnonymous()
    {
        var choice = SaleDocumentChoice.Create("electronicInvoice", "finalConsumer", null,
            null, null, 9999m);
        Assert.Equal(SaleRecipientTaxStatus.FinalConsumer, choice.RecipientTaxStatus);
    }

    [Fact]
    public void InvalidDocumentTypeAndMalformedIdentityAreRejected()
    {
        Assert.Throws<ArgumentException>(() => SaleDocumentChoice.Create(
            "unknown", "finalConsumer", null, null, null, 100m));
        Assert.Throws<ArgumentException>(() => SaleDocumentChoice.Create(
            "electronicInvoice", "registered", "Cliente", "20000000002", "Calle 123", 100m));
    }

    [Fact]
    public void ConfirmedSaleKeepsRequestedDocumentWithoutMarkingItIssued()
    {
        var choice = SaleDocumentChoice.Create("electronicInvoice", "registered", "Cliente",
            "20000000001", "Calle 123", 2500m);
        var sale = new ConfirmedSale(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 2500m, new string('A', 64), DateTimeOffset.UtcNow,
            documentChoice: choice);

        Assert.Equal(SaleDocumentType.ElectronicInvoice, sale.DocumentType);
        Assert.Equal("20000000001", sale.RecipientDocumentNumber);
        Assert.Equal(SaleRecipientTaxStatus.Registered, sale.RecipientTaxStatus);
    }
}
