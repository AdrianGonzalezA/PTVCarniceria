namespace Carnicerias.Infrastructure;

public sealed class CustomerSaleCharge
{
    private CustomerSaleCharge() { }

    public CustomerSaleCharge(Guid companyId, Guid branchId, Guid customerId, Guid saleId,
        Guid cashierId, Guid cashierShiftId, decimal amount, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || customerId == Guid.Empty ||
            saleId == Guid.Empty || cashierId == Guid.Empty || cashierShiftId == Guid.Empty)
            throw new ArgumentException("All account-charge references are required.");
        if (amount <= 0 || amount > 9_999_999_999.99m || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        CustomerId = customerId;
        SaleId = saleId;
        CashierId = cashierId;
        CashierShiftId = cashierShiftId;
        Amount = amount;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
