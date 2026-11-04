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

    [Theory]
    [InlineData("registered", 1)]
    [InlineData("finalConsumer", 6)]
    public void PreservesWeightedLineRoundingAndMixedTaxSnapshotInArcaRequest(
        string recipientStatus, int expectedVoucherType)
    {
        var companyId = Guid.NewGuid();
        var inputs = new[]
        {
            (Guid.NewGuid(), 0.125m, 10.60m, SaleTaxTreatment.Taxed, 21m),
            (Guid.NewGuid(), 0.125m, 10.60m, SaleTaxTreatment.Taxed, 21m),
            (Guid.NewGuid(), 1m, 1.01m, SaleTaxTreatment.Exempt, 0m)
        };
        var amounts = SaleAmountCalculator.Calculate(inputs.Select(line =>
            new SaleAmountInput(line.Item1, line.Item2, line.Item3, 0m,
                line.Item4, line.Item5)).ToArray(), 0m);
        var document = SaleDocumentChoice.Create("electronicInvoice", recipientStatus,
            recipientStatus == "finalConsumer" ? null : "Cliente de prueba",
            recipientStatus == "finalConsumer" ? null : "20000000001",
            recipientStatus == "finalConsumer" ? null : "Domicilio de prueba",
            amounts.Total);
        var sale = new ConfirmedSale(companyId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amounts.Total,
            new string('a', 64), DateTimeOffset.UtcNow,
            posTerminalId: Guid.NewGuid(), documentChoice: document);
        foreach (var (productId, quantity, unitPrice, treatment, rate) in inputs)
        {
            var line = new ConfirmedSaleLine(companyId, productId, productId.ToString("N"),
                "Corte", treatment == SaleTaxTreatment.Exempt ? "un" : "kg",
                treatment == SaleTaxTreatment.Exempt ? ProductSaleMode.Unit : ProductSaleMode.Weight,
                quantity, unitPrice);
            // Match the immutable line ID from the confirmed sale when calculating the snapshot.
            var amount = SaleAmountCalculator.Calculate([
                new SaleAmountInput(line.Id, quantity, unitPrice, 0m, treatment, rate)
            ], 0m).Lines[0];
            var rule = new ProductTaxRule(companyId, productId, treatment, rate,
                Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
            line.SetAmountSnapshot(amount, rule);
            sale.Lines.Add(line);
        }
        var request = FiscalRequestBuilder.Build(sale, 99, 27, new DateOnly(2026, 11, 4));

        Assert.Equal(expectedVoucherType, request.VoucherType);
        Assert.Equal(3.67m, request.Total);
        Assert.Equal(2.20m, request.TaxableBase);
        Assert.Equal(1.01m, request.ExemptAmount);
        Assert.Equal(0m, request.NotTaxedAmount);
        var vat = Assert.Single(request.VatAmounts);
        Assert.Equal(5, vat.ArcaRateCode);
        Assert.Equal(0.46m, vat.TaxAmount);
        Assert.Equal(request.Total,
            request.TaxableBase + vat.TaxAmount + request.ExemptAmount);
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
