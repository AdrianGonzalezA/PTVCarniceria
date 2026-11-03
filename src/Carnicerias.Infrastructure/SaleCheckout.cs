using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carnicerias.Domain.Sales;

namespace Carnicerias.Infrastructure;

public enum SaleCheckoutStatus { Prepared, Committed, PendingBalance, Ready, Completed }

public enum SaleCheckoutInstrumentStatus { Prepared, InProcess, Approved, Rejected, Uncertain }

public sealed record SaleCheckoutTerms(Guid? CustomerId, decimal AccountChargeAmount,
    decimal CreditAppliedAmount, bool AccountChargeConfirmed, string DocumentType,
    string RecipientTaxStatus, string? RecipientName = null, string? RecipientDocumentNumber = null,
    string? RecipientAddress = null, string? RecipientLocality = null, string? RecipientProvince = null,
    Guid? FiscalCustomerId = null, string? PurchaseOrderNumber = null);

public sealed record SaleCheckoutPayment(Guid Id, PaymentMethod Method, decimal Amount,
    string? Reference = null);

/// <summary>The durable payment plan. A confirmed sale is created only after every active operation settles.</summary>
public sealed class SaleCheckout
{
    private SaleCheckout() { TermsJson = string.Empty; PlanHash = string.Empty; }

    public SaleCheckout(Guid companyId, Guid branchId, Guid saleDraftId, Guid cashierShiftId,
        Guid cashierId, Guid posTerminalId, decimal total, DateTimeOffset draftUpdatedAtUtc,
        DateTimeOffset now)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || saleDraftId == Guid.Empty ||
            cashierShiftId == Guid.Empty || cashierId == Guid.Empty || posTerminalId == Guid.Empty ||
            total <= 0 || total > 9_999_999_999.99m || decimal.Round(total, 2) != total)
            throw new ArgumentException("A checkout needs an exact draft, shift, terminal and total.");
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        SaleDraftId = saleDraftId;
        CashierShiftId = cashierShiftId;
        CashierId = cashierId;
        PosTerminalId = posTerminalId;
        Total = total;
        DraftUpdatedAtUtc = draftUpdatedAtUtc.ToUniversalTime();
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
        TermsJson = string.Empty;
        PlanHash = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid SaleDraftId { get; private set; }
    public Guid CashierShiftId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid PosTerminalId { get; private set; }
    public decimal Total { get; private set; }
    public DateTimeOffset DraftUpdatedAtUtc { get; private set; }
    public SaleCheckoutStatus Status { get; private set; }
    public int Revision { get; private set; }
    public string TermsJson { get; private set; }
    public string PlanHash { get; private set; }
    public Guid? CommitKey { get; private set; }
    public Guid? ConfirmedSaleId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? CommittedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public List<SaleCheckoutInstrument> Instruments { get; private set; } = [];
    public List<SaleCheckoutEvent> Events { get; private set; } = [];
    public SaleCheckoutTerms Terms => JsonSerializer.Deserialize<SaleCheckoutTerms>(TermsJson)
        ?? throw new InvalidOperationException("Checkout terms are missing.");
    public decimal ApprovedAmount => Instruments.Where(item => item.Status == SaleCheckoutInstrumentStatus.Approved)
        .Sum(item => item.Amount);
    public decimal RemainingAmount => Math.Max(0, Total - Terms.AccountChargeAmount -
        Terms.CreditAppliedAmount - ApprovedAmount);

    public void Prepare(int expectedRevision, SaleCheckoutTerms terms,
        IReadOnlyList<SaleCheckoutPayment> payments, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(payments);
        if (Status != SaleCheckoutStatus.Prepared || expectedRevision != Revision ||
            payments.Count > 20 || payments.Any(item => item.Id == Guid.Empty ||
                item.Reference?.Length > 200) || payments.Select(item => item.Id).Distinct().Count() != payments.Count ||
            terms.CustomerId == Guid.Empty || terms.FiscalCustomerId == Guid.Empty ||
            (terms.CustomerId is null && (terms.AccountChargeAmount != 0 || terms.CreditAppliedAmount != 0)))
            throw new InvalidOperationException("Checkout preparation conflicts with its current revision.");
        PaymentSettlement.CalculateWithAccountCharge(Total,
            payments.Select(item => new PaymentTender(item.Method, item.Amount)).ToArray(),
            terms.AccountChargeAmount, terms.CreditAppliedAmount);
        Instruments.Clear();
        Instruments.AddRange(payments.Select(item => new SaleCheckoutInstrument(item)));
        TermsJson = JsonSerializer.Serialize(terms);
        PlanHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Total, DraftUpdatedAtUtc, TermsJson, Payments = payments }))));
        Revision++;
        UpdatedAtUtc = now.ToUniversalTime();
        Events.Add(new SaleCheckoutEvent("Prepared", "Prepared", null, CashierId,
            $"Revision {Revision}", UpdatedAtUtc));
    }

    public void Commit(int expectedRevision, Guid key, DateTimeOffset now)
    {
        if (key == Guid.Empty) throw new InvalidOperationException("Commit key is required.");
        if (CommitKey == key && Status != SaleCheckoutStatus.Prepared && expectedRevision == Revision) return;
        if (Status != SaleCheckoutStatus.Prepared || expectedRevision != Revision || Revision == 0)
            throw new InvalidOperationException("Checkout was already committed or revised.");
        CommitKey = key;
        Status = Instruments.Count == 0 ? SaleCheckoutStatus.Ready : SaleCheckoutStatus.Committed;
        CommittedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CommittedAtUtc.Value;
        Events.Add(new SaleCheckoutEvent("Prepared", Status.ToString(), null, CashierId,
            "Payment plan committed", UpdatedAtUtc));
    }

    public void ApproveManual(Guid instrumentId, string? reference, DateTimeOffset now)
    {
        var instrument = Find(instrumentId);
        if (instrument.Status == SaleCheckoutInstrumentStatus.Approved &&
            instrument.Reference == (reference?.Trim() ?? instrument.Reference)) return;
        EnsureCommitted();
        if (instrument.Method == PaymentMethod.MercadoPago)
            throw new InvalidOperationException("An integrated payment needs provider approval.");
        instrument.Approve(reference, now);
        Events.Add(new SaleCheckoutEvent("Prepared", "Approved", instrumentId, CashierId,
            instrument.Reference, now));
        RefreshStatus(now);
    }

    public void RejectManual(Guid instrumentId, string reason, DateTimeOffset now)
    {
        EnsureCommitted();
        var instrument = Find(instrumentId);
        if (instrument.Method == PaymentMethod.MercadoPago || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A manual payment needs a rejection reason.");
        instrument.Reject(now);
        Events.Add(new SaleCheckoutEvent("Prepared", "Rejected", instrumentId, CashierId,
            reason.Trim(), now));
        RefreshStatus(now);
    }

    public void AddReplacement(SaleCheckoutPayment payment, DateTimeOffset now)
    {
        EnsureCommitted();
        if (!Instruments.Any(item => item.Status == SaleCheckoutInstrumentStatus.Rejected) ||
            Instruments.Count >= 20 || payment.Id == Guid.Empty ||
            Instruments.Any(item => item.Id == payment.Id) || payment.Amount <= 0 ||
            payment.Amount > 9_999_999_999.99m || decimal.Round(payment.Amount, 2) != payment.Amount ||
            !Enum.IsDefined(payment.Method) || payment.Reference?.Length > 200)
            throw new InvalidOperationException("Only a rejected payment can be replaced.");
        Instruments.Add(new SaleCheckoutInstrument(payment));
        Events.Add(new SaleCheckoutEvent("Rejected", "Prepared", payment.Id, CashierId,
            "Replacement payment", now));
        RefreshStatus(now);
    }

    public void LinkMercadoPago(Guid instrumentId, Guid intentId, DateTimeOffset now)
    {
        if (Find(instrumentId).PointPaymentIntentId == intentId &&
            Find(instrumentId).Status is SaleCheckoutInstrumentStatus.InProcess or
                SaleCheckoutInstrumentStatus.Approved) return;
        EnsureCommitted();
        Find(instrumentId).LinkMercadoPago(intentId, now);
        Events.Add(new SaleCheckoutEvent("Prepared", "InProcess", instrumentId, CashierId,
            $"Intent {intentId:N}", now));
        RefreshStatus(now);
    }

    public void SyncMercadoPago(Guid instrumentId, PointPaymentStatus providerStatus,
        string? reference, DateTimeOffset now)
    {
        var instrument = Find(instrumentId);
        if (instrument.Status == SaleCheckoutInstrumentStatus.Approved &&
            providerStatus == PointPaymentStatus.Approved &&
            instrument.Reference == (reference ?? instrument.Reference)) return;
        EnsureCommitted();
        var before = instrument.Status;
        instrument.SyncMercadoPago(providerStatus, reference, now);
        if (before != instrument.Status)
            Events.Add(new SaleCheckoutEvent(before.ToString(), instrument.Status.ToString(),
                instrumentId, null, instrument.Reference, now));
        RefreshStatus(now);
    }

    public void Complete(Guid saleId, DateTimeOffset now)
    {
        if (Status == SaleCheckoutStatus.Completed && ConfirmedSaleId == saleId) return;
        if (Status != SaleCheckoutStatus.Ready || saleId == Guid.Empty)
            throw new InvalidOperationException("Every payment must be approved before sale confirmation.");
        ConfirmedSaleId = saleId;
        Status = SaleCheckoutStatus.Completed;
        CompletedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CompletedAtUtc.Value;
        Events.Add(new SaleCheckoutEvent("Ready", "Completed", null, CashierId,
            $"Sale {saleId:N}", UpdatedAtUtc));
    }

    private SaleCheckoutInstrument Find(Guid instrumentId) =>
        Instruments.SingleOrDefault(item => item.Id == instrumentId)
        ?? throw new InvalidOperationException("Payment operation was not found.");

    private void EnsureCommitted()
    {
        if (Status is not (SaleCheckoutStatus.Committed or SaleCheckoutStatus.PendingBalance))
            throw new InvalidOperationException("The payment plan is not awaiting operations.");
    }

    private void RefreshStatus(DateTimeOffset now)
    {
        var approved = Instruments.Where(item => item.Status == SaleCheckoutInstrumentStatus.Approved)
            .Select(item => new PaymentTender(item.Method, item.Amount)).ToArray();
        var allCertain = Instruments.All(item => item.Status is SaleCheckoutInstrumentStatus.Approved or
            SaleCheckoutInstrumentStatus.Rejected);
        try
        {
            PaymentSettlement.CalculateWithAccountCharge(Total, approved,
                Terms.AccountChargeAmount, Terms.CreditAppliedAmount);
            Status = allCertain ? SaleCheckoutStatus.Ready : SaleCheckoutStatus.PendingBalance;
        }
        catch (PaymentSettlementException error) when (error.Error == PaymentSettlementError.AmountPending)
        {
            Status = approved.Length == 0 ? SaleCheckoutStatus.Committed : SaleCheckoutStatus.PendingBalance;
        }
        UpdatedAtUtc = now.ToUniversalTime();
    }
}

public sealed class SaleCheckoutInstrument
{
    private SaleCheckoutInstrument() { }

    public SaleCheckoutInstrument(SaleCheckoutPayment payment)
    {
        if (payment.Id == Guid.Empty || !Enum.IsDefined(payment.Method) || payment.Amount <= 0 ||
            payment.Amount > 9_999_999_999.99m || decimal.Round(payment.Amount, 2) != payment.Amount ||
            payment.Reference?.Length > 200)
            throw new ArgumentException("Invalid payment operation.");
        Id = payment.Id;
        Method = payment.Method;
        Amount = payment.Amount;
        Reference = payment.Reference?.Trim();
    }

    public Guid Id { get; private set; }
    public Guid SaleCheckoutId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public SaleCheckoutInstrumentStatus Status { get; private set; }
    public string? Reference { get; private set; }
    public Guid? PointPaymentIntentId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public void Approve(string? reference, DateTimeOffset now)
    {
        if (Status != SaleCheckoutInstrumentStatus.Prepared || reference?.Length > 200)
            throw new InvalidOperationException("Payment has already been decided.");
        Reference = reference?.Trim() ?? Reference;
        Status = SaleCheckoutInstrumentStatus.Approved;
        ApprovedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = ApprovedAtUtc;
    }

    public void Reject(DateTimeOffset now)
    {
        if (Status != SaleCheckoutInstrumentStatus.Prepared)
            throw new InvalidOperationException("Payment has already been decided.");
        Status = SaleCheckoutInstrumentStatus.Rejected;
        UpdatedAtUtc = now.ToUniversalTime();
    }

    public void LinkMercadoPago(Guid intentId, DateTimeOffset now)
    {
        if (Method != PaymentMethod.MercadoPago || intentId == Guid.Empty ||
            Status is not (SaleCheckoutInstrumentStatus.Prepared or SaleCheckoutInstrumentStatus.InProcess) ||
            PointPaymentIntentId is not null && PointPaymentIntentId != intentId)
            throw new InvalidOperationException("Payment intent does not match this operation.");
        PointPaymentIntentId = intentId;
        Status = SaleCheckoutInstrumentStatus.InProcess;
        UpdatedAtUtc = now.ToUniversalTime();
    }

    public void SyncMercadoPago(PointPaymentStatus providerStatus, string? reference, DateTimeOffset now)
    {
        if (Method != PaymentMethod.MercadoPago || PointPaymentIntentId is null ||
            reference?.Length > 200)
            throw new InvalidOperationException("An unlinked operation cannot receive a provider result.");
        var next = providerStatus switch
        {
            PointPaymentStatus.Approved => SaleCheckoutInstrumentStatus.Approved,
            PointPaymentStatus.Rejected or PointPaymentStatus.Expired or PointPaymentStatus.Canceled =>
                SaleCheckoutInstrumentStatus.Rejected,
            PointPaymentStatus.NeedsReconciliation or PointPaymentStatus.Refunded =>
                SaleCheckoutInstrumentStatus.Uncertain,
            _ => SaleCheckoutInstrumentStatus.InProcess
        };
        if (Status == SaleCheckoutInstrumentStatus.Approved && next != SaleCheckoutInstrumentStatus.Approved)
            throw new InvalidOperationException("An accredited operation cannot be downgraded.");
        Status = next;
        Reference = reference ?? Reference;
        if (next == SaleCheckoutInstrumentStatus.Approved) ApprovedAtUtc ??= now.ToUniversalTime();
        UpdatedAtUtc = now.ToUniversalTime();
    }
}

public sealed class SaleCheckoutEvent
{
    private SaleCheckoutEvent() { FromStatus = string.Empty; ToStatus = string.Empty; }

    public SaleCheckoutEvent(string fromStatus, string toStatus, Guid? instrumentId,
        Guid? actorUserId, string? reference, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        FromStatus = fromStatus;
        ToStatus = toStatus;
        InstrumentId = instrumentId;
        ActorUserId = actorUserId;
        Reference = reference;
        CreatedAtUtc = now.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid SaleCheckoutId { get; private set; }
    public Guid? InstrumentId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string FromStatus { get; private set; }
    public string ToStatus { get; private set; }
    public string? Reference { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
