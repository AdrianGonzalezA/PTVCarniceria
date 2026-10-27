using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class CustomerCollectionPaymentTests
{
    [Fact]
    public void ReceiptPreservesCombinedPaymentsAndCashChange()
    {
        var receipt = NewReceipt(100m,
        [
            new AppliedPayment(PaymentMethod.Cash, 80m, 60m),
            new AppliedPayment(PaymentMethod.Debit, 40m, 40m)
        ]);

        Assert.Equal(2, receipt.Payments.Count);
        Assert.Equal(20m, receipt.ChangeAmount);
        Assert.Equal(100m, receipt.Payments.Sum(payment => payment.AppliedAmount));
    }

    [Fact]
    public void ReceiptRejectsACombinedPaymentThatDoesNotCoverTheCollection()
    {
        Assert.Throws<ArgumentException>(() => NewReceipt(100m,
        [
            new AppliedPayment(PaymentMethod.Cash, 30m, 30m),
            new AppliedPayment(PaymentMethod.Debit, 40m, 40m)
        ]));
    }

    [Fact]
    public void CashCanBeReturnedInFullWhenAnotherMethodCoversTheCollection()
    {
        var receipt = NewReceipt(100m,
        [
            new AppliedPayment(PaymentMethod.Cash, 10m, 0m),
            new AppliedPayment(PaymentMethod.Debit, 100m, 100m)
        ]);

        Assert.Equal(10m, receipt.ChangeAmount);
        Assert.Equal(0m, receipt.Payments.Single(payment => payment.Method == PaymentMethod.Cash).AppliedAmount);
    }

    private static CustomerCollectionReceipt NewReceipt(decimal amount,
        IReadOnlyList<AppliedPayment> payments) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), PaymentMethod.Cash,
        amount, 0m, DateTimeOffset.UtcNow, payments: payments);
}
