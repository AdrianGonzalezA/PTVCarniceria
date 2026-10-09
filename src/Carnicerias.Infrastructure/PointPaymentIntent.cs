namespace Carnicerias.Infrastructure;

public enum PointPaymentStatus
{
    Prepared,
    Pending,
    Approved,
    Rejected,
    Expired,
    Canceled,
    NeedsReconciliation,
    Refunded
}

/// <summary>A durable, non-cardholder record created before sending a Point order.</summary>
public sealed class PointPaymentIntent
{
    private PointPaymentIntent()
    {
        TerminalId = string.Empty;
    }

    public PointPaymentIntent(Guid companyId, Guid branchId, Guid saleDraftId,
        Guid cashierShiftId, Guid cashierId, Guid posTerminalId, string terminalId,
        decimal amount, DateTimeOffset now)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || saleDraftId == Guid.Empty ||
            cashierShiftId == Guid.Empty || cashierId == Guid.Empty || posTerminalId == Guid.Empty ||
            string.IsNullOrWhiteSpace(terminalId) || terminalId.Length > 100 ||
            !terminalId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
            amount <= 0 || amount > 999_999_999.99m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("A Point intent needs an exact sale, register, terminal and amount.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        SaleDraftId = saleDraftId;
        CashierShiftId = cashierShiftId;
        CashierId = cashierId;
        PosTerminalId = posTerminalId;
        TerminalId = terminalId;
        Amount = amount;
        IdempotencyKey = Guid.NewGuid();
        Status = PointPaymentStatus.Prepared;
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid SaleDraftId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid PosTerminalId { get; private set; }
    public string TerminalId { get; private set; }
    public decimal Amount { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public PointPaymentStatus Status { get; private set; }
    public string? ProviderOrderId { get; private set; }
    public string? ProviderPaymentId { get; private set; }
    public string? ProviderOrderStatus { get; private set; }
    public string? ProviderPaymentStatus { get; private set; }
    public string? ProviderPaymentStatusDetail { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string ExternalReference => Id.ToString("N");

    public void MarkUncertain(DateTimeOffset now)
    {
        if (Status is PointPaymentStatus.Approved or PointPaymentStatus.Refunded or
            PointPaymentStatus.Rejected or PointPaymentStatus.Expired or PointPaymentStatus.Canceled)
            throw new InvalidOperationException("A final Point outcome cannot become uncertain.");
        Status = PointPaymentStatus.NeedsReconciliation;
        UpdatedAtUtc = now.ToUniversalTime();
    }

    public void RecordProviderState(string orderId, string paymentId, string orderStatus,
        string paymentStatus, string? paymentStatusDetail, bool isApproved, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 100 ||
            string.IsNullOrWhiteSpace(paymentId) || paymentId.Length > 100 ||
            string.IsNullOrWhiteSpace(orderStatus) || orderStatus.Length > 40 ||
            string.IsNullOrWhiteSpace(paymentStatus) || paymentStatus.Length > 40 ||
            paymentStatusDetail?.Length > 80 ||
            (ProviderOrderId is not null && ProviderOrderId != orderId) ||
            (ProviderPaymentId is not null && ProviderPaymentId != paymentId))
            throw new InvalidOperationException("The Point order identity changed or is invalid.");
        if (isApproved && (orderStatus != "processed" || paymentStatus != "processed" ||
                           paymentStatusDetail != "accredited"))
            throw new InvalidOperationException("A Point payment is not accredited.");

        var next = orderStatus switch
        {
            "processed" when isApproved => PointPaymentStatus.Approved,
            "processed" => PointPaymentStatus.NeedsReconciliation,
            "failed" => PointPaymentStatus.Rejected,
            "expired" => PointPaymentStatus.Expired,
            "canceled" => PointPaymentStatus.Canceled,
            "refunded" => PointPaymentStatus.Refunded,
            "created" or "at_terminal" or "processing" => PointPaymentStatus.Pending,
            _ => PointPaymentStatus.NeedsReconciliation
        };
        if (Status is PointPaymentStatus.Approved && next is not PointPaymentStatus.Approved and
            not PointPaymentStatus.Refunded ||
            Status is PointPaymentStatus.Refunded && next != PointPaymentStatus.Refunded)
            throw new InvalidOperationException("A final Point payment cannot be downgraded.");

        ProviderOrderId = orderId;
        ProviderPaymentId = paymentId;
        ProviderOrderStatus = orderStatus;
        ProviderPaymentStatus = paymentStatus;
        ProviderPaymentStatusDetail = paymentStatusDetail;
        Status = next;
        UpdatedAtUtc = now.ToUniversalTime();
    }
}
