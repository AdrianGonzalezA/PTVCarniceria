using Carnicerias.Api.Fiscal;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

public sealed class FiscalReconciliationTests
{
    [Fact]
    public void FiscalAttemptKeepsIssuerDetailsForLaterReprints()
    {
        var attempt = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), "30710106513",
            99, 6, 7, new DateOnly(2026, 10, 9), 1210m, 96, 12345678,
            DateTimeOffset.UtcNow, "Emisor al emitir", "Domicilio al emitir", "12345",
            new DateOnly(2020, 1, 1));

        Assert.Equal("Emisor al emitir", attempt.IssuerName);
        Assert.Equal("Domicilio al emitir", attempt.IssuerAddress);
        Assert.Equal("12345", attempt.IssuerIibb);
        Assert.Equal(new DateOnly(2020, 1, 1), attempt.IssuerActivityStartDate);
    }

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

    [Theory]
    [InlineData(36, 37, true)]
    [InlineData(37, 37, false)]
    [InlineData(38, 37, false)]
    public void ReleasesAnUncertainNumberOnlyWhenArcaConfirmsItIsAbsent(
        long lastAuthorized, long attemptedNumber, bool expected)
    {
        var observedAt = new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);
        var attempt = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), "30710106513",
            99, 1, attemptedNumber, new DateOnly(2026, 11, 11), 5000m,
            80, 30502736473, observedAt.AddMinutes(-3));
        attempt.RequireReconciliation();

        Assert.Equal(expected, FiscalDocumentEndpoints.CanConfirmNotIssued(
            attempt, [602], lastAuthorized, observedAt));
        Assert.False(FiscalDocumentEndpoints.CanConfirmNotIssued(attempt, [500], lastAuthorized, observedAt));
        Assert.False(FiscalDocumentEndpoints.CanConfirmNotIssued(attempt, [602, 500], lastAuthorized, observedAt));
        Assert.False(FiscalDocumentEndpoints.CanConfirmNotIssued(attempt, [602], lastAuthorized,
            attempt.CreatedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void ReconciledAbsentAttemptPreservesHistoryAndAllowsANewAttempt()
    {
        var attempt = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), "30710106513",
            99, 1, 37, new DateOnly(2026, 11, 11), 5000m,
            80, 30502736473, DateTimeOffset.UtcNow);
        attempt.RequireReconciliation();

        attempt.ConfirmNotIssued();

        Assert.Equal(FiscalDocumentStatus.Rejected, attempt.Status);
        Assert.Equal("602", attempt.ErrorCodes);
        Assert.Null(attempt.Cae);
        Assert.Throws<InvalidOperationException>(attempt.ConfirmNotIssued);
    }

    [Fact]
    public void RejectedAttemptsDoNotReserveTheNumberWhileUncertainAttemptsStillDo()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=unused")
            .Options;
        using var db = new PlatformAccessDbContext(options);
        var entity = db.Model.FindEntityType(typeof(FiscalDocument))!;
        var index = entity.GetIndexes().Single(item => item.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(FiscalDocument.IssuerCuit), nameof(FiscalDocument.PointOfSale),
                nameof(FiscalDocument.VoucherType), nameof(FiscalDocument.Number)]));

        Assert.True(index.IsUnique);
        Assert.Equal("\"Status\" <> 2", index.GetFilter());
    }

    [Fact]
    public void DoesNotReconcileAnOldAttemptUnderAnotherFiscalSequenceLock()
    {
        var attempt = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), "30710106513",
            99, 1, 37, new DateOnly(2026, 11, 11), 5000m,
            80, 30502736473, DateTimeOffset.UtcNow);

        Assert.True(FiscalDocumentEndpoints.IsSameSequence(attempt, "30710106513", 99, 1));
        Assert.False(FiscalDocumentEndpoints.IsSameSequence(attempt, "30710106513", 100, 1));
        Assert.False(FiscalDocumentEndpoints.IsSameSequence(attempt, "30710106513", 99, 6));
        Assert.False(FiscalDocumentEndpoints.IsSameSequence(attempt, "30502736473", 99, 1));
    }
}
