using Carnicerias.Domain.Sales;

namespace Carnicerias.Infrastructure;

public sealed class ConfirmedSale
{
    private ConfirmedSale() => PaymentRequestHash = string.Empty;

    public ConfirmedSale(Guid companyId, Guid branchId, Guid cashierId, Guid cashierShiftId,
        Guid sourceDraftId, Guid priceListId, decimal total, string paymentRequestHash,
        DateTimeOffset confirmedAtUtc, Guid? posTerminalId = null)
    {
        if (posTerminalId == Guid.Empty)
            throw new ArgumentException("Terminal id cannot be empty.", nameof(posTerminalId));
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
    public List<ConfirmedSaleLine> Lines { get; private set; } = [];
    public List<SalePayment> Payments { get; private set; } = [];
}

public sealed class ConfirmedSaleLine
{
    private ConfirmedSaleLine() { ProductCode = string.Empty; ProductName = string.Empty; Unit = string.Empty; }

    public ConfirmedSaleLine(Guid companyId, Guid productId, string code, string name, string unit,
        ProductSaleMode saleMode, decimal quantity, decimal unitPrice)
    {
        CompanyId = companyId;
        ProductId = productId;
        ProductCode = code;
        ProductName = name;
        Unit = unit;
        SaleMode = saleMode;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
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
    Change
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
