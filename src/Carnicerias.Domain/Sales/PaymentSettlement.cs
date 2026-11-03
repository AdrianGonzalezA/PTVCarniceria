namespace Carnicerias.Domain.Sales;

public enum PaymentMethod
{
    Cash,
    Debit,
    Credit,
    Transfer,
    MercadoPago,
    Cheque
}

public enum PaymentSettlementError
{
    InvalidAmount,
    InvalidPaymentMethod,
    AmountPending,
    NonCashOverpayment,
    DuplicateMethod
}

public sealed class PaymentSettlementException(PaymentSettlementError error)
    : InvalidOperationException("The payment amounts cannot settle this sale.")
{
    public PaymentSettlementError Error { get; } = error;
}

public sealed record PaymentTender(PaymentMethod Method, decimal TenderedAmount);

public sealed record AppliedPayment(PaymentMethod Method, decimal TenderedAmount, decimal AppliedAmount);

public sealed record PaymentSettlementResult(
    decimal SaleTotal,
    IReadOnlyList<AppliedPayment> AppliedPayments,
    decimal ChangeAmount);

public static class PaymentSettlement
{
    private const decimal MaximumAmount = 9_999_999_999.99m;
    private const int MaximumOperations = 20;

    public static PaymentSettlementResult CalculateWithAccountCharge(
        decimal saleTotal, IReadOnlyList<PaymentTender> tenders, decimal accountCharge,
        decimal creditApplied = 0)
    {
        ArgumentNullException.ThrowIfNull(tenders);
        ValidateAmount(saleTotal);
        ValidateAmount(accountCharge);
        ValidateAmount(creditApplied);
        if (saleTotal == 0 || accountCharge + creditApplied > saleTotal)
            throw new PaymentSettlementException(PaymentSettlementError.InvalidAmount);

        var immediateTotal = saleTotal - accountCharge - creditApplied;
        if (immediateTotal == 0 && tenders.Count == 0)
            return new PaymentSettlementResult(saleTotal, [], 0);

        var immediate = Calculate(immediateTotal, tenders);
        return new PaymentSettlementResult(saleTotal, immediate.AppliedPayments, immediate.ChangeAmount);
    }

    public static PaymentSettlementResult Calculate(
        decimal saleTotal,
        IReadOnlyList<PaymentTender> tenders)
    {
        ArgumentNullException.ThrowIfNull(tenders);
        ValidateAmount(saleTotal);
        if (saleTotal == 0 || tenders.Count is < 1 or > MaximumOperations)
            throw new PaymentSettlementException(PaymentSettlementError.AmountPending);

        decimal nonCashTotal = 0;
        decimal cashTendered = 0;
        var cashCount = 0;
        foreach (var tender in tenders)
        {
            if (!Enum.IsDefined(tender.Method))
                throw new PaymentSettlementException(PaymentSettlementError.InvalidPaymentMethod);
            ValidateAmount(tender.TenderedAmount);
            if (tender.TenderedAmount == 0)
                throw new PaymentSettlementException(PaymentSettlementError.InvalidAmount);

            if (tender.Method == PaymentMethod.Cash)
            {
                cashTendered += tender.TenderedAmount;
                cashCount++;
            }
            else
                nonCashTotal += tender.TenderedAmount;
        }

        if (nonCashTotal > saleTotal)
            throw new PaymentSettlementException(PaymentSettlementError.NonCashOverpayment);

        var cashApplied = saleTotal - nonCashTotal;
        if (cashCount == 0 && cashApplied != 0 || cashCount > 0 &&
            (cashTendered < cashApplied || cashApplied < cashCount * 0.01m))
            throw new PaymentSettlementException(PaymentSettlementError.AmountPending);

        var remainingCashApplied = cashApplied;
        var remainingCashCount = cashCount;
        var appliedPayments = tenders.Select(tender =>
        {
            if (tender.Method != PaymentMethod.Cash)
                return new AppliedPayment(tender.Method, tender.TenderedAmount, tender.TenderedAmount);
            remainingCashCount--;
            var applied = Math.Min(tender.TenderedAmount,
                remainingCashApplied - remainingCashCount * 0.01m);
            remainingCashApplied -= applied;
            return new AppliedPayment(tender.Method, tender.TenderedAmount, applied);
        }).ToArray();
        var change = cashTendered - cashApplied;
        return new PaymentSettlementResult(saleTotal, appliedPayments, change);
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount < 0 || amount > MaximumAmount || decimal.Round(amount, 2) != amount)
            throw new PaymentSettlementException(PaymentSettlementError.InvalidAmount);
    }
}
