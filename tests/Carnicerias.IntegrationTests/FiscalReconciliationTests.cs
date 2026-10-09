using Carnicerias.Api.Fiscal;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class FiscalReconciliationTests
{
    [Fact]
    public void AcceptsOnlyTheSameAuthorizedInvoiceOnASharedPointOfSale()
    {
        var date = new DateOnly(2026, 10, 9);
        var attempt = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), "30710106513",
            99, 6, 3, date, 1210m, 96, 12345678, DateTimeOffset.UtcNow);
        var same = new ArcaInvoiceLookup(99, 6, 3, 1210m, "A", "12345678901234",
            "CAE", date.AddDays(10), 96, 12345678, date);
        Assert.True(FiscalDocumentEndpoints.Matches(attempt, same));
        Assert.False(FiscalDocumentEndpoints.Matches(attempt, same with { ReceiverDocumentNumber = 87654321 }));
        Assert.False(FiscalDocumentEndpoints.Matches(attempt, same with { Total = 1200m }));
        Assert.False(FiscalDocumentEndpoints.Matches(attempt, same with { IssueDate = date.AddDays(1) }));
        Assert.False(FiscalDocumentEndpoints.Matches(attempt, same with { AuthorizationKind = "CAEA" }));
        Assert.False(FiscalDocumentEndpoints.Matches(attempt, same with { Result = "R" }));
    }
}
