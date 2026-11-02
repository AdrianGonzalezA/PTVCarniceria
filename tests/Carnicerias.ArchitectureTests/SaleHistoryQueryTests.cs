using Carnicerias.Api.History;
using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class SaleHistoryQueryTests
{
    [Theory]
    [InlineData("nonFiscalTicket", SaleDocumentType.NonFiscalTicket)]
    [InlineData("fiscalTicket", SaleDocumentType.FiscalTicket)]
    [InlineData("electronicInvoice", SaleDocumentType.ElectronicInvoice)]
    public void ParsesOnlyKnownDocumentTypes(string value, SaleDocumentType expected)
    {
        Assert.True(SaleHistoryQuery.TryParseDocumentType(value, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("FiscalTicket")]
    [InlineData("other")]
    public void RejectsUnknownDocumentTypes(string value)
    {
        Assert.False(SaleHistoryQuery.TryParseDocumentType(value, out _));
    }

    [Fact]
    public void MissingDocumentTypeKeepsAllSales()
    {
        Assert.True(SaleHistoryQuery.TryParseDocumentType(null, out var parsed));
        Assert.Null(parsed);

        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            Sale(companyId, branchId, terminalId, at, SaleDocumentType.NonFiscalTicket),
            Sale(companyId, branchId, terminalId, at, SaleDocumentType.FiscalTicket),
            Sale(companyId, branchId, terminalId, at, SaleDocumentType.ElectronicInvoice)
        };

        var scoped = SaleHistoryQuery.ScopeSales(rows.AsQueryable(), companyId, null, null,
            null, null, parsed).ToArray();

        Assert.Equal(3, scoped.Length);
    }

    [Fact]
    public void FiltersDocumentTypeTogetherWithCompanyBranchTerminalAndPeriodBeforePaging()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var rows = new[]
        {
            Sale(companyId, branchId, terminalId, from.AddHours(1), SaleDocumentType.ElectronicInvoice),
            Sale(companyId, branchId, terminalId, from.AddHours(2), SaleDocumentType.FiscalTicket),
            Sale(companyId, branchId, terminalId, from.AddHours(3), SaleDocumentType.ElectronicInvoice),
            Sale(companyId, branchId, terminalId, to, SaleDocumentType.ElectronicInvoice),
            Sale(companyId, branchId, Guid.NewGuid(), from.AddHours(4), SaleDocumentType.ElectronicInvoice),
            Sale(companyId, Guid.NewGuid(), terminalId, from.AddHours(5), SaleDocumentType.ElectronicInvoice),
            Sale(Guid.NewGuid(), branchId, terminalId, from.AddHours(6), SaleDocumentType.ElectronicInvoice)
        };

        var scoped = SaleHistoryQuery.ScopeSales(rows.AsQueryable(), companyId, branchId,
            terminalId, from, to, SaleDocumentType.ElectronicInvoice);
        var firstPage = SaleHistoryQuery.PageSales(scoped, 1, 1).ToArray();
        var secondPage = SaleHistoryQuery.PageSales(scoped, 2, 1).ToArray();

        Assert.Equal(2, scoped.Count());
        Assert.Same(rows[2], Assert.Single(firstPage));
        Assert.Same(rows[0], Assert.Single(secondPage));
    }

    private static ConfirmedSale Sale(Guid companyId, Guid branchId, Guid terminalId,
        DateTimeOffset at, SaleDocumentType type) =>
        new(companyId, branchId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 100m, "test", at, terminalId,
            documentChoice: new SaleDocumentChoice(type, SaleRecipientTaxStatus.FinalConsumer,
                null, null, null));
}
