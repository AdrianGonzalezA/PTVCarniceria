using Carnicerias.Api.Fiscal;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class FiscalRequestBuilderTests
{
    [Fact]
    public void BuildsAInvoiceFromTheImmutableSaleTaxSnapshot()
    {
        var sale = Sale("registered", "20000000001", SaleTaxTreatment.Taxed, 21m);

        var request = FiscalRequestBuilder.Build(sale, 99, 17, new DateOnly(2026, 10, 9));

        Assert.Equal(1, request.VoucherType);
        Assert.Equal(80, request.ReceiverDocumentType);
        Assert.Equal(20000000001, request.ReceiverDocumentNumber);
        Assert.Equal(2500m, request.Total);
        Assert.Single(request.VatAmounts);
        Assert.Equal(5, request.VatAmounts[0].ArcaRateCode);
        Assert.Equal(request.Total, request.TaxableBase + request.VatAmounts[0].TaxAmount);
    }

    [Fact]
    public void BuildsBInvoiceForAnonymousFinalConsumer()
    {
        var sale = Sale("finalConsumer", null, SaleTaxTreatment.Taxed, 21m);

        var request = FiscalRequestBuilder.Build(sale, 99, 18, new DateOnly(2026, 10, 9));

        Assert.Equal(6, request.VoucherType);
        Assert.Equal(99, request.ReceiverDocumentType);
        Assert.Equal(0, request.ReceiverDocumentNumber);
    }

    [Fact]
    public void RefusesAnUnclassifiedSaleEvenInHomologation()
    {
        var sale = Sale("finalConsumer", null, null, 0m);

        Assert.Throws<FiscalSaleNotReadyException>(() =>
            FiscalRequestBuilder.Build(sale, 99, 19, new DateOnly(2026, 10, 9)));
    }

    [Fact]
    public void RefusesToGuessAnUnmappedArcaVatCode()
    {
        var sale = Sale("finalConsumer", null, SaleTaxTreatment.Taxed, 17m);

        Assert.Throws<FiscalSaleNotReadyException>(() =>
            FiscalRequestBuilder.Build(sale, 99, 19, new DateOnly(2026, 10, 9)));
    }

    private static ConfirmedSale Sale(string recipientStatus, string? recipientDocument,
        SaleTaxTreatment? treatment, decimal rate)
    {
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var choice = SaleDocumentChoice.Create("electronicInvoice", recipientStatus,
            recipientStatus == "finalConsumer" ? null : "Cliente de prueba",
            recipientDocument, recipientStatus == "finalConsumer" ? null : "Domicilio de prueba", 2500m);
        var sale = new ConfirmedSale(companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 2500m, new string('a', 64), DateTimeOffset.UtcNow,
            posTerminalId: Guid.NewGuid(), documentChoice: choice);
        var line = new ConfirmedSaleLine(companyId, productId, "TEST", "Artículo de prueba",
            "un", ProductSaleMode.Unit, 1m, 2500m);
        ProductTaxRule? rule = treatment is null ? null : new ProductTaxRule(companyId, productId,
            treatment.Value, rate, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
        var amount = SaleAmountCalculator.Calculate(
            [new SaleAmountInput(line.Id, 1m, 2500m, 0m, treatment ?? SaleTaxTreatment.NotTaxed, rate)], 0m);
        line.SetAmountSnapshot(amount.Lines[0], rule);
        sale.Lines.Add(line);
        return sale;
    }
}
