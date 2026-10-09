using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class PointPaymentIntentTests
{
    private static PointPaymentIntent NewIntent() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), "TERMINAL_1", 2375m, DateTimeOffset.UtcNow);

    [Fact]
    public void PersistsStableReferenceAndRetryKeyBeforeContactingProvider()
    {
        var intent = NewIntent();
        Assert.Equal(intent.Id.ToString("N"), intent.ExternalReference);
        Assert.NotEqual(Guid.Empty, intent.IdempotencyKey);
        Assert.Equal(PointPaymentStatus.Prepared, intent.Status);
        Assert.Null(intent.ProviderOrderId);
    }

    [Fact]
    public void TimeoutDoesNotBecomeApprovedAndMayLaterReconcile()
    {
        var intent = NewIntent();
        var now = DateTimeOffset.UtcNow;
        intent.MarkUncertain(now);
        Assert.Equal(PointPaymentStatus.NeedsReconciliation, intent.Status);
        intent.RecordProviderState("ORD1", "PAY1", "created", "created", null, false, now);
        Assert.Equal(PointPaymentStatus.Pending, intent.Status);
        intent.RecordProviderState("ORD1", "PAY1", "processed", "processed", "accredited", true, now);
        Assert.Equal(PointPaymentStatus.Approved, intent.Status);
        Assert.Equal("ORD1", intent.ProviderOrderId);
    }

    [Fact]
    public void CannotReplaceProviderOrderOrDowngradeApprovedPayment()
    {
        var intent = NewIntent();
        var now = DateTimeOffset.UtcNow;
        intent.RecordProviderState("ORD1", "PAY1", "processed", "processed", "accredited", true, now);
        Assert.Throws<InvalidOperationException>(() => intent.RecordProviderState(
            "ORD2", "PAY1", "processed", "processed", "accredited", true, now));
        Assert.Throws<InvalidOperationException>(() => intent.MarkUncertain(now));
        Assert.Throws<InvalidOperationException>(() => intent.RecordProviderState(
            "ORD1", "PAY1", "created", "created", null, false, now));
    }
}
