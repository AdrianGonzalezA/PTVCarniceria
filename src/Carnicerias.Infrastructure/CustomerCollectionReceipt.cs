using Carnicerias.Domain.Sales;

namespace Carnicerias.Infrastructure;

public enum CollectionReceiptOrigin
{
    CashReceived,
    Reallocation
}

public sealed class CustomerCollectionReceipt
{
    private CustomerCollectionReceipt() => RequestHash = string.Empty;

    public CustomerCollectionReceipt(Guid companyId, Guid branchId, Guid customerId, Guid cashierId,
        Guid cashierShiftId, Guid? posTerminalId, Guid operationId, string requestHash,
        PaymentMethod method, decimal amount, decimal creditAmount, DateTimeOffset createdAtUtc,
        CollectionReceiptOrigin origin = CollectionReceiptOrigin.CashReceived,
        Guid? replacesReceiptId = null, IReadOnlyList<AppliedPayment>? payments = null)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || customerId == Guid.Empty ||
            cashierId == Guid.Empty || cashierShiftId == Guid.Empty || operationId == Guid.Empty ||
            posTerminalId == Guid.Empty)
            throw new ArgumentException("All collection references are required.");
        if (requestHash.Length != 64)
            throw new ArgumentException("A SHA-256 request hash is required.", nameof(requestHash));
        if (!Enum.IsDefined(method))
            throw new ArgumentOutOfRangeException(nameof(method));
        if (!Enum.IsDefined(origin) || (origin == CollectionReceiptOrigin.Reallocation) !=
            replacesReceiptId.HasValue || replacesReceiptId == Guid.Empty)
            throw new ArgumentException("A reallocated receipt must reference its original receipt.");
        if (amount <= 0 || amount > 9_999_999_999.99m || decimal.Round(amount, 2) != amount ||
            creditAmount < 0 || creditAmount > amount || decimal.Round(creditAmount, 2) != creditAmount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        payments ??= [new AppliedPayment(method, amount, amount)];
        if (payments.Count is < 1 or > 6 || !payments.Any(payment => payment.Method == method) ||
            payments.Select(payment => payment.Method).Distinct().Count() != payments.Count ||
            payments.Any(payment => !Enum.IsDefined(payment.Method) ||
                payment.AppliedAmount < 0 || payment.TenderedAmount < payment.AppliedAmount ||
                payment.Method != PaymentMethod.Cash && payment.AppliedAmount == 0 ||
                payment.Method != PaymentMethod.Cash && payment.TenderedAmount != payment.AppliedAmount ||
                decimal.Round(payment.AppliedAmount, 2) != payment.AppliedAmount ||
                decimal.Round(payment.TenderedAmount, 2) != payment.TenderedAmount) ||
            payments.Sum(payment => payment.AppliedAmount) != amount)
            throw new ArgumentException("Collection payments must cover the exact receipt amount.", nameof(payments));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        CustomerId = customerId;
        CashierId = cashierId;
        CashierShiftId = cashierShiftId;
        PosTerminalId = posTerminalId;
        OperationId = operationId;
        RequestHash = requestHash;
        Method = method;
        Amount = amount;
        CreditAmount = creditAmount;
        Origin = origin;
        ReplacesReceiptId = replacesReceiptId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Payments.AddRange(payments.Select(payment => new CustomerCollectionPayment(
            payment.Method, payment.TenderedAmount, payment.AppliedAmount)));
    }

    public Guid Id { get; private set; }
    public long ReceiptNumber { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public Guid? PosTerminalId { get; private set; }
    public Guid OperationId { get; private set; }
    public string RequestHash { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public decimal CreditAmount { get; private set; }
    public CollectionReceiptOrigin Origin { get; private set; }
    public Guid? ReplacesReceiptId { get; private set; }
    public bool IsVoided { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public List<CustomerCollectionAllocation> Allocations { get; private set; } = [];
    public List<CustomerCollectionPayment> Payments { get; private set; } = [];
    public decimal ChangeAmount => Payments.Sum(payment => payment.TenderedAmount - payment.AppliedAmount);

    public void Void(DateTimeOffset atUtc)
    {
        if (IsVoided) throw new InvalidOperationException("This collection has already been corrected.");
        IsVoided = true;
        VoidedAtUtc = atUtc.ToUniversalTime();
    }
}

public sealed class CustomerCollectionPayment
{
    private CustomerCollectionPayment() { }

    public CustomerCollectionPayment(PaymentMethod method, decimal tenderedAmount, decimal appliedAmount)
    {
        if (!Enum.IsDefined(method) || tenderedAmount <= 0 || appliedAmount < 0 ||
            appliedAmount > tenderedAmount ||
            method != PaymentMethod.Cash && appliedAmount == 0 ||
            (method != PaymentMethod.Cash && tenderedAmount != appliedAmount) ||
            decimal.Round(tenderedAmount, 2) != tenderedAmount ||
            decimal.Round(appliedAmount, 2) != appliedAmount)
            throw new ArgumentException("A collection payment needs a valid method and amount.");
        Id = Guid.NewGuid();
        Method = method;
        TenderedAmount = tenderedAmount;
        AppliedAmount = appliedAmount;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal TenderedAmount { get; private set; }
    public decimal AppliedAmount { get; private set; }
    public CustomerCollectionReceipt Receipt { get; private set; } = null!;
}

public sealed class CustomerCollectionAllocation
{
    private CustomerCollectionAllocation() { }

    public CustomerCollectionAllocation(Guid companyId, Guid customerId, Guid saleId, decimal amount)
    {
        if (companyId == Guid.Empty || customerId == Guid.Empty || saleId == Guid.Empty ||
            amount <= 0 || amount > 9_999_999_999.99m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("A positive allocation and its references are required.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        CustomerId = customerId;
        SaleId = saleId;
        Amount = amount;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid SaleId { get; private set; }
    public decimal Amount { get; private set; }
    public CustomerCollectionReceipt Receipt { get; private set; } = null!;
}
