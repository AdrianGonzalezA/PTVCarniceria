using Carnicerias.Domain.Sales;

namespace Carnicerias.Infrastructure;

public sealed class ConfirmedSale
{
    private ConfirmedSale() => PaymentRequestHash = string.Empty;

    public ConfirmedSale(Guid companyId, Guid branchId, Guid cashierId, Guid cashierShiftId,
        Guid sourceDraftId, Guid priceListId, decimal total, string paymentRequestHash,
        DateTimeOffset confirmedAtUtc, Guid? posTerminalId = null,
        Guid? customerId = null, decimal accountChargeAmount = 0,
        string? customerCode = null, string? customerName = null,
        decimal creditAppliedAmount = 0, decimal discountAmount = 0,
        string? discountReason = null)
    {
        if (posTerminalId == Guid.Empty)
            throw new ArgumentException("Terminal id cannot be empty.", nameof(posTerminalId));
        if (customerId == Guid.Empty || accountChargeAmount < 0 || creditAppliedAmount < 0 ||
            accountChargeAmount + creditAppliedAmount > total ||
            decimal.Round(accountChargeAmount, 2) != accountChargeAmount ||
            decimal.Round(creditAppliedAmount, 2) != creditAppliedAmount ||
            (accountChargeAmount + creditAppliedAmount > 0 && customerId is null))
            throw new ArgumentException("Account settlement requires a valid customer and amount.");
        if (accountChargeAmount + creditAppliedAmount > 0 && (string.IsNullOrWhiteSpace(customerCode) ||
            string.IsNullOrWhiteSpace(customerName) || customerCode.Length > 80 || customerName.Length > 200))
            throw new ArgumentException("Account settlement requires a customer identity snapshot.");
        if (total <= 0 || discountAmount < 0 || decimal.Round(discountAmount, 2) != discountAmount ||
            (discountAmount > 0 && (string.IsNullOrWhiteSpace(discountReason) ||
                discountReason.Trim().Length is < 10 or > 200)))
            throw new ArgumentException("A discounted sale requires a valid amount and reason.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        CashierId = cashierId;
        CashierShiftId = cashierShiftId;
        PosTerminalId = posTerminalId;
        SourceDraftId = sourceDraftId;
        PriceListId = priceListId;
        Total = total;
        PaymentRequestHash = paymentRequestHash;
        ConfirmedAtUtc = confirmedAtUtc.ToUniversalTime();
        CustomerId = customerId;
        AccountChargeAmount = accountChargeAmount;
        CreditAppliedAmount = creditAppliedAmount;
        CustomerCode = customerCode;
        CustomerName = customerName;
        DiscountAmount = discountAmount;
        DiscountReason = discountAmount > 0 ? discountReason?.Trim() : null;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public Guid? PosTerminalId { get; private set; }
    public Guid SourceDraftId { get; private set; }
    public Guid PriceListId { get; private set; }
    public decimal Total { get; private set; }
    public string PaymentRequestHash { get; private set; }
    public DateTimeOffset ConfirmedAtUtc { get; private set; }
    public Guid? CustomerId { get; private set; }
    public decimal AccountChargeAmount { get; private set; }
    public decimal CreditAppliedAmount { get; private set; }
    public string? CustomerCode { get; private set; }
    public string? CustomerName { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public string? DiscountReason { get; private set; }
    public List<ConfirmedSaleLine> Lines { get; private set; } = [];
    public List<SalePayment> Payments { get; private set; } = [];
}

public sealed class ConfirmedSaleLine
{
    private ConfirmedSaleLine() { ProductCode = string.Empty; ProductName = string.Empty; Unit = string.Empty; }

    public ConfirmedSaleLine(Guid companyId, Guid productId, string code, string name, string unit,
        ProductSaleMode saleMode, decimal quantity, decimal unitPrice,
        Guid? inventoryPieceId = null, string? pieceIdentifier = null)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        ProductId = productId;
        ProductCode = code;
        ProductName = name;
        Unit = unit;
        SaleMode = saleMode;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        InventoryPieceId = inventoryPieceId;
        PieceIdentifier = pieceIdentifier;
    }

    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; }
    public string ProductName { get; private set; }
    public string Unit { get; private set; }
    public ProductSaleMode SaleMode { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public Guid? InventoryPieceId { get; private set; }
    public string? PieceIdentifier { get; private set; }
    public decimal OrderDiscountAmount { get; private set; }
    public decimal? NetAfterDiscount { get; private set; }
    public Guid? TaxRuleId { get; private set; }
    public SaleTaxTreatment? TaxTreatment { get; private set; }
    public decimal? TaxRatePercent { get; private set; }
    public decimal? TaxableBase { get; private set; }
    public decimal? TaxAmount { get; private set; }

    public void SetAmountSnapshot(SaleLineAmount amount, ProductTaxRule? rule)
    {
        if (amount.LineId != Id || amount.GrossBeforeDiscount != LineTotal ||
            amount.LineDiscount != 0 || amount.Total < 0 ||
            amount.OrderDiscount < 0 || amount.Total + amount.OrderDiscount != LineTotal ||
            (rule is not null && (rule.CompanyId != CompanyId || rule.ProductId != ProductId ||
                rule.Treatment != amount.TaxTreatment || rule.RatePercent != amount.TaxRatePercent)))
            throw new ArgumentException("The amount snapshot does not match the confirmed line.", nameof(amount));
        OrderDiscountAmount = amount.OrderDiscount;
        NetAfterDiscount = amount.Total;
        TaxRuleId = rule?.Id;
        TaxTreatment = rule?.Treatment;
        TaxRatePercent = rule?.RatePercent;
        TaxableBase = rule is null ? null : amount.TaxableBase;
        TaxAmount = rule is null ? null : amount.TaxAmount;
    }
}

public sealed class SalePayment
{
    private SalePayment() { }

    public SalePayment(PaymentMethod method, decimal tenderedAmount, decimal appliedAmount)
    {
        Method = method;
        TenderedAmount = tenderedAmount;
        AppliedAmount = appliedAmount;
    }

    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal TenderedAmount { get; private set; }
    public decimal AppliedAmount { get; private set; }
}

public enum CashLedgerMovementKind
{
    Opening,
    SalePayment,
    Change,
    AccountCollection,
    AccountCollectionRefund
}

public sealed class CashLedgerMovement
{
    private CashLedgerMovement() { }

    public CashLedgerMovement(Guid companyId, Guid branchId, Guid shiftId, Guid cashierId,
        Guid operationId, PaymentMethod method, CashLedgerMovementKind kind,
        decimal amountDelta, DateTimeOffset createdAtUtc, Guid? saleId = null,
        Guid? posTerminalId = null)
    {
        if (posTerminalId == Guid.Empty)
            throw new ArgumentException("Terminal id cannot be empty.", nameof(posTerminalId));
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        CashierShiftId = shiftId;
        PosTerminalId = posTerminalId;
        CashierId = cashierId;
        OperationId = operationId;
        Method = method;
        Kind = kind;
        AmountDelta = amountDelta;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        SaleId = saleId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public Guid? PosTerminalId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid? SaleId { get; private set; }
    public Guid OperationId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public CashLedgerMovementKind Kind { get; private set; }
    public decimal AmountDelta { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
