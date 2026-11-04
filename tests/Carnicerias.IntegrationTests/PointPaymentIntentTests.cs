using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class PointPaymentIntentTests
{
    [Fact]
    public void CollectionOrderCannotBeAppliedToASaleOrAnotherReceipt()
    {
        var customerId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var intent = PointPaymentIntent.ForCollection(Guid.NewGuid(), Guid.NewGuid(),
            customerId, operationId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "SUC1CAJA1", 125m, DateTimeOffset.UtcNow, MercadoPagoOrderMode.DynamicQr);

        Assert.Null(intent.SaleDraftId);
        Assert.Equal(customerId, intent.CustomerId);
        Assert.Equal(operationId, intent.CustomerCollectionOperationId);
        intent.RecordProviderState("ORD1", "PAY1", "processed", "processed", "accredited",
            true, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => intent.ApplyToSale(Guid.NewGuid()));
        var receiptId = Guid.NewGuid();
        intent.ApplyToCollection(receiptId);
        Assert.Equal(receiptId, intent.ConfirmedCollectionReceiptId);
        Assert.Throws<InvalidOperationException>(() => intent.ApplyToCollection(Guid.NewGuid()));
    }

    [Fact]
    public void DynamicQrCanBeAppliedOnceOnlyAfterAccreditation()
    {
        var intent = new PointPaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SUC1CAJA1", 125m,
            DateTimeOffset.UtcNow, MercadoPagoOrderMode.DynamicQr);
        intent.SetQrData("000201TEST");
        Assert.Throws<InvalidOperationException>(() => intent.ApplyToSale(Guid.NewGuid()));
        intent.RecordProviderState("ORD1", "PAY1", "processed", "processed", "accredited",
            true, DateTimeOffset.UtcNow);
        var saleId = Guid.NewGuid();
        intent.ApplyToSale(saleId);
        Assert.Equal(saleId, intent.ConfirmedSaleId);
        Assert.Throws<InvalidOperationException>(() => intent.ApplyToSale(Guid.NewGuid()));
    }

    [Fact]
    public void StaticQrPersistsItsPermanentCodeAndCreatedOrderWithoutPaymentStatus()
    {
        var intent = new PointPaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SUC1CAJA1", 125m,
            DateTimeOffset.UtcNow, MercadoPagoOrderMode.StaticQr);
        intent.SetQrData("000201STATIC");
        intent.RecordProviderState("ORD1", "PAY1", "created", null, null,
            false, DateTimeOffset.UtcNow);
        Assert.Equal(PointPaymentStatus.Pending, intent.Status);
        Assert.Equal("000201STATIC", intent.QrData);
        Assert.Null(intent.ProviderPaymentStatus);
    }

    [Fact]
    public void StaticQrPreflightFailureReleasesOnlyAnOrderThatWasNeverSent()
    {
        var intent = new PointPaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SUC1CAJA1", 125m,
            DateTimeOffset.UtcNow, MercadoPagoOrderMode.StaticQr);
        intent.RejectBeforeProviderOrder(DateTimeOffset.UtcNow);
        Assert.Equal(PointPaymentStatus.Rejected, intent.Status);
        Assert.Null(intent.ProviderOrderId);
        Assert.Throws<InvalidOperationException>(() =>
            intent.RejectBeforeProviderOrder(DateTimeOffset.UtcNow));

        var claimed = new PointPaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SUC1CAJA1", 125m,
            DateTimeOffset.UtcNow, MercadoPagoOrderMode.StaticQr);
        Assert.True(claimed.TryClaimProviderCreate(DateTimeOffset.UtcNow));
        claimed.RejectBeforeProviderOrder(DateTimeOffset.UtcNow);
        Assert.Equal(PointPaymentStatus.Rejected, claimed.Status);
    }

    [Fact]
    public void AnHttpFailureDoesNotProveThatTheProviderOrderWasRejected()
    {
        var intent = new PointPaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SUC1CAJA1", 125m,
            DateTimeOffset.UtcNow, MercadoPagoOrderMode.DynamicQr);
        intent.MarkUncertain(DateTimeOffset.UtcNow);
        Assert.Equal(PointPaymentStatus.NeedsReconciliation, intent.Status);
        Assert.Null(intent.ProviderOrderId);
    }

    [Fact]
    public void ProviderCreateClaimBlocksConcurrentCallsAndKeepsTheSameIdempotencyKey()
    {
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var intent = new PointPaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SUC1CAJA1", 125m,
            now, MercadoPagoOrderMode.DynamicQr);
        var originalKey = intent.IdempotencyKey;

        Assert.True(intent.TryClaimProviderCreate(now));
        Assert.Equal(PointPaymentStatus.Pending, intent.Status);
        Assert.False(intent.TryClaimProviderCreate(now.AddSeconds(30)));
        Assert.True(intent.TryClaimProviderCreate(now.AddSeconds(61)));
        Assert.Equal(originalKey, intent.IdempotencyKey);
        intent.MarkUncertain(now.AddSeconds(62));
        Assert.True(intent.TryClaimProviderCreate(now.AddSeconds(63)));
        Assert.False(intent.TryClaimProviderCreate(now.AddHours(23)));
    }

    [Fact]
    public void KnownOrFinalProviderOutcomesCannotBeClaimedAgain()
    {
        var now = DateTimeOffset.UtcNow;
        var intent = new PointPaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SUC1CAJA1", 125m,
            now, MercadoPagoOrderMode.DynamicQr);
        Assert.True(intent.TryClaimProviderCreate(now));
        intent.RecordProviderState("ORD1", "PAY1", "created", "created", null,
            false, now.AddSeconds(1));
        Assert.False(intent.TryClaimProviderCreate(now.AddMinutes(2)));
        intent.RecordProviderState("ORD1", "PAY1", "processed", "processed", "accredited",
            true, now.AddMinutes(3));
        Assert.False(intent.TryClaimProviderCreate(now.AddMinutes(4)));
    }

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
