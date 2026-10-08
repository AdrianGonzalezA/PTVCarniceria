using Carnicerias.Domain.Sales;

namespace Carnicerias.IntegrationTests;

public sealed class PaymentSettlementTests
{
    [Fact]
    public void CalculatesChangeOnlyFromCash()
    {
        var settlement = PaymentSettlement.Calculate(875m,
        [
            new PaymentTender(PaymentMethod.Cash, 1000m)
        ]);

        Assert.Equal(125m, settlement.ChangeAmount);
        Assert.Equal(875m, Assert.Single(settlement.AppliedPayments).AppliedAmount);
    }

    [Fact]
    public void SettlesCombinedPaymentsAndReturnsOnlyCashExcess()
    {
        var settlement = PaymentSettlement.Calculate(875m,
        [
            new PaymentTender(PaymentMethod.Cash, 400m),
            new PaymentTender(PaymentMethod.Debit, 600m)
        ]);

        Assert.Equal(125m, settlement.ChangeAmount);
        Assert.Equal(275m, settlement.AppliedPayments.Single(payment => payment.Method == PaymentMethod.Cash).AppliedAmount);
        Assert.Equal(600m, settlement.AppliedPayments.Single(payment => payment.Method == PaymentMethod.Debit).AppliedAmount);
    }

    [Fact]
    public void RejectsNonCashOverpayment()
    {
        var error = Assert.Throws<PaymentSettlementException>(() =>
            PaymentSettlement.Calculate(875m, [new PaymentTender(PaymentMethod.Debit, 900m)]));

        Assert.Equal(PaymentSettlementError.NonCashOverpayment, error.Error);
    }

    [Fact]
    public void RejectsAnUnpaidRemainder()
    {
        var error = Assert.Throws<PaymentSettlementException>(() =>
            PaymentSettlement.Calculate(875m, [new PaymentTender(PaymentMethod.Cash, 800m)]));

        Assert.Equal(PaymentSettlementError.AmountPending, error.Error);
    }

    [Fact]
    public void RejectsDuplicatePaymentMethods()
    {
        var error = Assert.Throws<PaymentSettlementException>(() => PaymentSettlement.Calculate(875m,
        [
            new PaymentTender(PaymentMethod.Debit, 400m),
            new PaymentTender(PaymentMethod.Debit, 475m)
        ]));

        Assert.Equal(PaymentSettlementError.DuplicateMethod, error.Error);
    }

    [Fact]
    public void CombinesImmediatePaymentAndAccountChargeWithoutTreatingDebtAsCash()
    {
        var settlement = PaymentSettlement.CalculateWithAccountCharge(875m,
            [new PaymentTender(PaymentMethod.Cash, 400m)], 475m);

        Assert.Equal(875m, settlement.SaleTotal);
        Assert.Equal(0m, settlement.ChangeAmount);
        Assert.Equal(400m, Assert.Single(settlement.AppliedPayments).AppliedAmount);
    }

    [Fact]
    public void AllowsFullAccountChargeWithoutImmediatePayment()
    {
        var settlement = PaymentSettlement.CalculateWithAccountCharge(875m, [], 875m);

        Assert.Empty(settlement.AppliedPayments);
        Assert.Equal(0m, settlement.ChangeAmount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(875.01)]
    [InlineData(1.001)]
    public void RejectsInvalidAccountCharge(decimal charge)
    {
        var error = Assert.Throws<PaymentSettlementException>(() =>
            PaymentSettlement.CalculateWithAccountCharge(875m, [], charge));

        Assert.Equal(PaymentSettlementError.InvalidAmount, error.Error);
    }

    [Fact]
    public void AccountChargeCannotHideMissingImmediatePayment()
    {
        var error = Assert.Throws<PaymentSettlementException>(() =>
            PaymentSettlement.CalculateWithAccountCharge(875m,
                [new PaymentTender(PaymentMethod.Cash, 300m)], 475m));

        Assert.Equal(PaymentSettlementError.AmountPending, error.Error);
    }

    [Fact]
    public void AccountChargeCannotTurnNonCashOverpaymentIntoChange()
    {
        var error = Assert.Throws<PaymentSettlementException>(() =>
            PaymentSettlement.CalculateWithAccountCharge(875m,
                [new PaymentTender(PaymentMethod.Debit, 500m)], 475m));

        Assert.Equal(PaymentSettlementError.NonCashOverpayment, error.Error);
    }
}
