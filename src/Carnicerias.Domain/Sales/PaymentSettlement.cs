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
    private const int MaximumMethods = 6;

    public static PaymentSettlementResult Calculate(
        decimal saleTotal,
        IReadOnlyList<PaymentTender> tenders)
    {
        ArgumentNullException.ThrowIfNull(tenders);
        ValidateAmount(saleTotal);
        if (saleTotal == 0 || tenders.Count is < 1 or > MaximumMethods)
            throw new PaymentSettlementException(PaymentSettlementError.AmountPending);

        var usedMethods = new HashSet<PaymentMethod>();
        decimal nonCashTotal = 0;
        PaymentTender? cashTender = null;
        foreach (var tender in tenders)
        {
            if (!Enum.IsDefined(tender.Method))
                throw new PaymentSettlementException(PaymentSettlementError.InvalidPaymentMethod);
            if (!usedMethods.Add(tender.Method))
                throw new PaymentSettlementException(PaymentSettlementError.DuplicateMethod);
            ValidateAmount(tender.TenderedAmount);
            if (tender.TenderedAmount == 0)
                throw new PaymentSettlementException(PaymentSettlementError.InvalidAmount);

            if (tender.Method == PaymentMethod.Cash)
                cashTender = tender;
            else
                nonCashTotal += tender.TenderedAmount;
        }

        if (nonCashTotal > saleTotal)
            throw new PaymentSettlementException(PaymentSettlementError.NonCashOverpayment);

        var cashApplied = saleTotal - nonCashTotal;
        if (cashTender is null && cashApplied != 0 || cashTender is not null && cashTender.TenderedAmount < cashApplied)
            throw new PaymentSettlementException(PaymentSettlementError.AmountPending);

        var appliedPayments = tenders.Select(tender => new AppliedPayment(
            tender.Method,
            tender.TenderedAmount,
            tender.Method == PaymentMethod.Cash ? cashApplied : tender.TenderedAmount)).ToArray();
        var change = cashTender is null ? 0 : cashTender.TenderedAmount - cashApplied;
        return new PaymentSettlementResult(saleTotal, appliedPayments, change);
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount < 0 || amount > MaximumAmount || decimal.Round(amount, 2) != amount)
            throw new PaymentSettlementException(PaymentSettlementError.InvalidAmount);
    }
}
