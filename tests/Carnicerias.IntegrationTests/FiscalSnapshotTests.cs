using Carnicerias.Api.Fiscal;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

public sealed class FiscalSnapshotTests
{
    private static readonly DateOnly IssueDate = new(2026, 11, 4);

    [Fact]
    public void CopiesTheExactRequestAmountsAndVatGroupsForOneAttempt()
    {
        var document = Attempt(342m);
        var request = Request(342m, 43,
            [new ArcaVatAmount(5, 100m, 21m), new ArcaVatAmount(4, 200m, 21m)]);

        var snapshot = FiscalSnapshotFactory.Create(document, request);
        document.AttachSnapshot(snapshot);

        Assert.Same(snapshot, document.Snapshot);
        Assert.Equal(document.Id, snapshot.FiscalDocumentId);
        Assert.Equal(342m, snapshot.Total);
        Assert.Equal(300m, snapshot.TaxableBase);
        Assert.Equal(42m, snapshot.VatAmount);
        Assert.Equal(0m, snapshot.ExemptAmount);
        Assert.Equal(0m, snapshot.NotTaxedAmount);
        Assert.Equal(5, snapshot.ReceiverVatConditionCode);
        Assert.Collection(snapshot.VatLines.OrderBy(line => line.ArcaRateCode),
            line =>
            {
                Assert.Equal(4, line.ArcaRateCode);
                Assert.Equal(10.5m, line.RatePercent);
                Assert.Equal(200m, line.TaxableBase);
                Assert.Equal(21m, line.TaxAmount);
            },
            line =>
            {
                Assert.Equal(5, line.ArcaRateCode);
                Assert.Equal(21m, line.RatePercent);
                Assert.Equal(100m, line.TaxableBase);
                Assert.Equal(21m, line.TaxAmount);
            });
    }

    [Fact]
    public void RejectsARequestForAnotherNumberOrTotal()
    {
        var document = Attempt(121m);

        Assert.Throws<ArgumentException>(() => FiscalSnapshotFactory.Create(document,
            Request(121m, 44, [new ArcaVatAmount(5, 100m, 21m)])));
        Assert.Throws<ArgumentException>(() => FiscalSnapshotFactory.Create(document,
            Request(242m, 43, [new ArcaVatAmount(5, 200m, 42m)])));
    }

    [Fact]
    public void PreventsReplacingTheSnapshotOfAnAttempt()
    {
        var document = Attempt(121m);
        var request = Request(121m, 43, [new ArcaVatAmount(5, 100m, 21m)]);
        document.AttachSnapshot(FiscalSnapshotFactory.Create(document, request));

        Assert.Throws<InvalidOperationException>(() => document.AttachSnapshot(
            FiscalSnapshotFactory.Create(document, request)));
    }

    [Fact]
    public void DistinguishesOlderAttemptsWithoutSnapshotFromNewOnes()
    {
        Assert.Equal("notIssued", FiscalDocumentEndpoints.SnapshotStatus(null));
        var historical = Attempt(121m);
        Assert.Equal("legacyIncomplete", FiscalDocumentEndpoints.SnapshotStatus(historical));

        historical.AttachSnapshot(FiscalSnapshotFactory.Create(historical,
            Request(121m, 43, [new ArcaVatAmount(5, 100m, 21m)])));
        Assert.Equal("complete", FiscalDocumentEndpoints.SnapshotStatus(historical));
    }

    [Fact]
    public void TracksAttemptSnapshotAndVatLinesAsOneInsertGraphWithoutDatabaseAccess()
    {
        var document = Attempt(342m);
        document.AttachSnapshot(FiscalSnapshotFactory.Create(document,
            Request(342m, 43,
                [new ArcaVatAmount(5, 100m, 21m), new ArcaVatAmount(4, 200m, 21m)])));
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=design_only")
            .Options;
        using var db = new PlatformAccessDbContext(options);

        db.FiscalDocuments.Add(document);

        Assert.Single(db.ChangeTracker.Entries<FiscalDocument>());
        Assert.Single(db.ChangeTracker.Entries<FiscalDocumentSnapshot>());
        Assert.Equal(2, db.ChangeTracker.Entries<FiscalDocumentVatLine>().Count());
        Assert.All(db.ChangeTracker.Entries(), entry =>
            Assert.Equal(EntityState.Added, entry.State));
    }

    private static FiscalDocument Attempt(decimal total) => new(Guid.NewGuid(), Guid.NewGuid(),
        "30710106513", 99, 6, 43, IssueDate, total, 99, 0,
        new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero));

    private static ArcaCaeRequest Request(decimal total, long number,
        IReadOnlyList<ArcaVatAmount> vat) => new(99, 6, number, IssueDate,
            99, 0, 5, total, vat.Sum(line => line.TaxableBase), 0m, 0m, vat);
}
