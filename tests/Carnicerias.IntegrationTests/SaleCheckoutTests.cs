using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class SaleCheckoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PreparationDoesNotApproveOrChargeAndCommitIsIdempotent()
    {
        var checkout = NewCheckout(300m);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        checkout.Prepare(0, Terms(),
            [new(first, PaymentMethod.Debit, 100m), new(second, PaymentMethod.Debit, 200m)], Now);

        Assert.Equal(SaleCheckoutStatus.Prepared, checkout.Status);
        Assert.Equal(0m, checkout.ApprovedAmount);
        Assert.Equal(1, checkout.Revision);
        Assert.All(checkout.Instruments, item => Assert.Equal(SaleCheckoutInstrumentStatus.Prepared, item.Status));

        var key = Guid.NewGuid();
        checkout.Commit(1, key, Now);
        checkout.Commit(1, key, Now);
        Assert.Equal(SaleCheckoutStatus.Committed, checkout.Status);
        Assert.Throws<InvalidOperationException>(() => checkout.Commit(1, Guid.NewGuid(), Now));
        Assert.Throws<InvalidOperationException>(() => checkout.Prepare(1, Terms(), [], Now));
    }

    [Fact]
    public void ApprovedPartialsRemainIndependentAndRecoverable()
    {
        var checkout = NewCheckout(300m);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        checkout.Prepare(0, Terms(),
            [new(first, PaymentMethod.Transfer, 100m), new(second, PaymentMethod.Transfer, 200m)], Now);
        checkout.Commit(1, Guid.NewGuid(), Now);
        checkout.ApproveManual(first, "transfer-001", Now);

        Assert.Equal(SaleCheckoutStatus.PendingBalance, checkout.Status);
        Assert.Equal(100m, checkout.ApprovedAmount);
        Assert.Equal(200m, checkout.RemainingAmount);
        Assert.Throws<InvalidOperationException>(() => checkout.Complete(Guid.NewGuid(), Now));
        checkout.ApproveManual(second, "transfer-002", Now);
        checkout.ApproveManual(second, "transfer-002", Now);
        Assert.Equal(SaleCheckoutStatus.Ready, checkout.Status);
        Assert.Equal("transfer-001", checkout.Instruments.Single(item => item.Id == first).Reference);
        Assert.Equal("transfer-002", checkout.Instruments.Single(item => item.Id == second).Reference);

        var saleId = Guid.NewGuid();
        checkout.Complete(saleId, Now);
        checkout.Complete(saleId, Now);
        Assert.Equal(SaleCheckoutStatus.Completed, checkout.Status);
        Assert.Equal(saleId, checkout.ConfirmedSaleId);
        Assert.Throws<InvalidOperationException>(() => checkout.Complete(Guid.NewGuid(), Now));
    }

    [Fact]
    public void RejectedOperationCanBeReplacedWithoutLosingApprovedOne()
    {
        var checkout = NewCheckout(300m);
        var accepted = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        checkout.Prepare(0, Terms(),
            [new(accepted, PaymentMethod.Debit, 100m), new(rejected, PaymentMethod.Credit, 200m)], Now);
        checkout.Commit(1, Guid.NewGuid(), Now);
        checkout.ApproveManual(accepted, "debit-001", Now);
        checkout.RejectManual(rejected, "rejected", Now);
        checkout.AddReplacement(new(replacement, PaymentMethod.Credit, 200m), Now);
        checkout.ApproveManual(replacement, "credit-002", Now);

        Assert.Equal(SaleCheckoutStatus.Ready, checkout.Status);
        Assert.Equal(SaleCheckoutInstrumentStatus.Approved,
            checkout.Instruments.Single(item => item.Id == accepted).Status);
        Assert.Equal(SaleCheckoutInstrumentStatus.Rejected,
            checkout.Instruments.Single(item => item.Id == rejected).Status);
        Assert.Equal(3, checkout.Instruments.Count);
    }

    [Fact]
    public void ProviderOutcomeMustBeCertainBeforeCompletion()
    {
        var checkout = NewCheckout(200m);
        var operation = Guid.NewGuid();
        checkout.Prepare(0, Terms(), [new(operation, PaymentMethod.MercadoPago, 200m)], Now);
        checkout.Commit(1, Guid.NewGuid(), Now);
        var intentId = Guid.NewGuid();
        checkout.LinkMercadoPago(operation, intentId, Now);
        checkout.SyncMercadoPago(operation, PointPaymentStatus.NeedsReconciliation,
            "order-001", Now);
        checkout.LinkMercadoPago(operation, intentId, Now);
        Assert.Equal(SaleCheckoutStatus.Committed, checkout.Status);
        Assert.Throws<InvalidOperationException>(() => checkout.Complete(Guid.NewGuid(), Now));

        checkout.SyncMercadoPago(operation, PointPaymentStatus.Approved, "order-001", Now);
        checkout.SyncMercadoPago(operation, PointPaymentStatus.Approved, "order-001", Now);
        Assert.Equal(SaleCheckoutStatus.Ready, checkout.Status);
        Assert.Equal(intentId, checkout.Instruments.Single().PointPaymentIntentId);
    }

    [Fact]
    public void PreparedCheckoutCanBeCancelledButCommittedPaymentCannot()
    {
        var checkout = NewCheckout(100m);
        checkout.Prepare(0, Terms(), [new(Guid.NewGuid(), PaymentMethod.Cash, 100m)], Now);
        checkout.CancelPrepared(Now);
        Assert.Equal(SaleCheckoutStatus.Canceled, checkout.Status);
        Assert.Throws<InvalidOperationException>(() => checkout.Commit(1, Guid.NewGuid(), Now));

        var committed = NewCheckout(100m);
        committed.Prepare(0, Terms(), [new(Guid.NewGuid(), PaymentMethod.Cash, 100m)], Now);
        committed.Commit(1, Guid.NewGuid(), Now);
        Assert.Throws<InvalidOperationException>(() => committed.CancelPrepared(Now));
    }

    [Fact]
    public void CardInstrumentKeepsItsOwnNonSensitiveDetails()
    {
        var checkout = NewCheckout(100m);
        var details = new SalePaymentInstrumentDetails(CardBrand: "Visa", Processor: "Clover",
            Terminal: "T1", Batch: "12", Voucher: "34", Authorization: "ABC123");
        checkout.Prepare(0, Terms(),
            [new(Guid.NewGuid(), PaymentMethod.Debit, 100m, "ABC123", details)], Now);

        Assert.Equal(details, checkout.Instruments.Single().Details);
        Assert.Equal("ABC123", checkout.Instruments.Single().Reference);
        Assert.Throws<InvalidOperationException>(() => checkout.Prepare(1, Terms(),
            [new(Guid.NewGuid(), PaymentMethod.Cash, 100m, null, details)], Now));
    }

    [Fact]
    public void TransferAndChequeRequireTheirOwnFieldsAndDates()
    {
        var transfer = new SalePaymentInstrumentDetails(Entity: "Banco", Date: "2026-11-11",
            Reference: "TR-123");
        var cheque = new SalePaymentInstrumentDetails(Bank: "Banco", Branch: "001",
            Number: "12345", Holder: "Titular", HolderCuit: "20164755100",
            IssuedOn: "2026-11-10", PayableOn: "2026-11-20");
        var checkout = NewCheckout(200m);
        checkout.Prepare(0, Terms(),
            [new(Guid.NewGuid(), PaymentMethod.Transfer, 100m, "TR-123", transfer),
             new(Guid.NewGuid(), PaymentMethod.Cheque, 100m, "12345", cheque)], Now);

        Assert.Equal(transfer, checkout.Instruments.Single(item => item.Method == PaymentMethod.Transfer).Details);
        Assert.Equal(cheque, checkout.Instruments.Single(item => item.Method == PaymentMethod.Cheque).Details);
        Assert.False((cheque with { PayableOn = "2026-11-01" }).IsValidFor(PaymentMethod.Cheque));
        Assert.False((transfer with { Entity = null }).IsValidFor(PaymentMethod.Transfer));
    }

    private static SaleCheckout NewCheckout(decimal total) => new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), total, Now, Now);

    private static SaleCheckoutTerms Terms() => new(null, 0m, 0m, false,
        "nonFiscalTicket", "finalConsumer");
}
