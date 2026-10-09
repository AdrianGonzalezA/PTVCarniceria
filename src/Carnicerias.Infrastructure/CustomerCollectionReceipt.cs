using Carnicerias.Domain.Sales;

namespace Carnicerias.Infrastructure;

public sealed class CustomerCollectionReceipt
{
    private CustomerCollectionReceipt() => RequestHash = string.Empty;

    public CustomerCollectionReceipt(Guid companyId, Guid branchId, Guid customerId, Guid cashierId,
        Guid cashierShiftId, Guid? posTerminalId, Guid operationId, string requestHash,
        PaymentMethod method, decimal amount, decimal creditAmount, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || customerId == Guid.Empty ||
            cashierId == Guid.Empty || cashierShiftId == Guid.Empty || operationId == Guid.Empty ||
            posTerminalId == Guid.Empty)
            throw new ArgumentException("All collection references are required.");
        if (requestHash.Length != 64)
            throw new ArgumentException("A SHA-256 request hash is required.", nameof(requestHash));
        if (!Enum.IsDefined(method))
            throw new ArgumentOutOfRangeException(nameof(method));
        if (amount <= 0 || amount > 9_999_999_999.99m || decimal.Round(amount, 2) != amount ||
            creditAmount < 0 || creditAmount > amount || decimal.Round(creditAmount, 2) != creditAmount)
            throw new ArgumentOutOfRangeException(nameof(amount));

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
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
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
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public List<CustomerCollectionAllocation> Allocations { get; private set; } = [];
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
}
