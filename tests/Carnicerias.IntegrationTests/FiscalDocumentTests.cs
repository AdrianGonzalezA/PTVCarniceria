using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class FiscalDocumentTests
{
    private static FiscalDocument Prepared() => new(Guid.NewGuid(), Guid.NewGuid(),
        "30710106513", 12, 6, 43, new DateOnly(2026, 10, 9), 2500m, 99, 0,
        new DateTimeOffset(2026, 10, 9, 13, 0, 0, TimeSpan.Zero));

    [Fact]
    public void PersistsTheExactAttemptBeforeContactingArca()
    {
        var document = Prepared();

        Assert.Equal(FiscalDocumentStatus.Prepared, document.Status);
        Assert.Equal(43, document.Number);
        Assert.Null(document.Cae);
    }

    [Fact]
    public void DoesNotTreatAnUncertainResponseAsAuthorized()
    {
        var document = Prepared();

        document.RequireReconciliation();

        Assert.Equal(FiscalDocumentStatus.NeedsReconciliation, document.Status);
        Assert.Null(document.Cae);
    }

    [Fact]
    public void AuthorizesOnlyMatchingCaeAndKeepsItImmutable()
    {
        var document = Prepared();
        document.RequireReconciliation();
        document.Authorize(12, 6, 43, "12345678901234", new DateOnly(2026, 10, 19),
            new DateTimeOffset(2026, 10, 9, 13, 1, 0, TimeSpan.Zero));

        Assert.Equal(FiscalDocumentStatus.Authorized, document.Status);
        Assert.Equal("12345678901234", document.Cae);
        Assert.Throws<InvalidOperationException>(() => document.RequireReconciliation());
        Assert.Throws<InvalidOperationException>(() => document.Authorize(12, 6, 43,
            "12345678901234", new DateOnly(2026, 10, 19), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RejectsAnAuthorizationForAnotherFiscalNumber()
    {
        var document = Prepared();

        Assert.Throws<ArgumentException>(() => document.Authorize(12, 6, 44,
            "12345678901234", new DateOnly(2026, 10, 19), DateTimeOffset.UtcNow));
        Assert.Equal(FiscalDocumentStatus.Prepared, document.Status);
    }
}
