using System.Data;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Sales;

public static class SaleCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapSaleCheckoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sales/drafts/{draftId:guid}/checkout");
        group.MapGet("", GetAsync).RequireOperationalContext();
        group.MapPut("", PrepareAsync).RequireOperationalContext();
        group.MapPost("/{checkoutId:guid}/commit", CommitAsync).RequireOperationalContext();
        group.MapPost("/{checkoutId:guid}/instruments", AddReplacementAsync).RequireOperationalContext();
        group.MapPost("/{checkoutId:guid}/instruments/{instrumentId:guid}/approve", ApproveAsync)
            .RequireOperationalContext();
        group.MapPost("/{checkoutId:guid}/instruments/{instrumentId:guid}/reject", RejectAsync)
            .RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid draftId, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var checkout = await OwnedAsync(db, accessor, draftId, null, false, cancellationToken);
        return checkout is null ? Results.NoContent() : Results.Ok(ToResponse(checkout));
    }

    private static async Task<IResult> PrepareAsync(Guid draftId, PrepareRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (request?.Payments is null || request.Payments.Count > 20 ||
            request.Payments.Any(item => item is null || item.Id == Guid.Empty ||
                item.Reference?.Length > 200 || !TryParseMethod(item.Method, out var method) ||
                item.Details is not null && !item.Details.IsValidFor(method)) ||
            request.Terms is null || !ValidTerms(request.Terms) || accessor.TerminalId is null)
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            if (!await SaleDraftTransactionLock.AcquireAsync(db, context.CompanyId,
                    context.BranchId, draftId, cancellationToken))
                return Error(404, "DRAFT_NOT_FOUND");
            var draft = await db.SaleDrafts.Include(item => item.Lines).SingleOrDefaultAsync(item =>
                item.Id == draftId && item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.UserId == context.UserId &&
                item.PosTerminalId == accessor.TerminalId && item.Status == SaleDraftStatus.Draft,
                cancellationToken);
            var shift = await OpenShiftAsync(db, accessor, cancellationToken);
            if (draft is null || draft.Lines.Count == 0) return Error(404, "DRAFT_NOT_FOUND");
            if (shift is null || draft.CashierShiftId != shift.Id)
                return Error(409, "CASHIER_SHIFT_REQUIRED");
            if (draft.TotalAfterDiscount <= 0) return Error(409, "SALE_NOT_CONFIRMABLE");
            if (draft.DiscountAmount > 0 &&
                !context.Permissions.Contains(PlatformPermissionCatalog.PosDiscountApply))
                return Error(403, "DISCOUNT_FORBIDDEN");
            var termsError = await ValidateTermsAsync(request.Terms, draft.TotalAfterDiscount,
                db, accessor, cancellationToken);
            if (termsError is not null) return termsError;
            var checkout = await OwnedAsync(db, accessor, draftId, null, true, cancellationToken);
            if (checkout is null)
            {
                if (request.ExpectedRevision != 0 || await db.PointPaymentIntents.AnyAsync(item =>
                    item.CompanyId == context.CompanyId && item.SaleDraftId == draftId &&
                    (item.Status == PointPaymentStatus.Prepared ||
                     item.Status == PointPaymentStatus.Pending ||
                     item.Status == PointPaymentStatus.Approved ||
                     item.Status == PointPaymentStatus.NeedsReconciliation), cancellationToken))
                    return Error(409, "CHECKOUT_CONFLICT");
                checkout = new SaleCheckout(context.CompanyId, context.BranchId, draftId,
                    shift.Id, context.UserId, accessor.TerminalId.Value, draft.TotalAfterDiscount,
                    draft.UpdatedAtUtc, clock.GetUtcNow());
                db.SaleCheckouts.Add(checkout);
            }
            else if (checkout.Status != SaleCheckoutStatus.Prepared ||
                     checkout.Revision != request.ExpectedRevision || checkout.CashierShiftId != shift.Id)
                return Error(409, "CHECKOUT_CONFLICT");
            else if (checkout.Instruments.Count > 0)
            {
                db.SaleCheckoutInstruments.RemoveRange(checkout.Instruments);
                await db.SaveChangesAsync(cancellationToken);
            }

            try
            {
                checkout.Prepare(request.ExpectedRevision, request.Terms,
                    request.Payments.Select(item => new SaleCheckoutPayment(item.Id,
                        ParseMethod(item.Method!), item.Amount, item.Reference, item.Details)).ToArray(),
                    clock.GetUtcNow(), draft.TotalAfterDiscount, draft.UpdatedAtUtc);
            }
            catch (Exception exception) when (exception is ArgumentException or
                InvalidOperationException or PaymentSettlementException)
            {
                return Error(400, "CHECKOUT_PLAN_INVALID");
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(checkout));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "CHECKOUT_CONFLICT");
        }
    }

    private static async Task<IResult> CommitAsync(Guid draftId, Guid checkoutId,
        CommitRequest? request, PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request is null || request.Key == Guid.Empty || checkoutId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            if (!await SaleDraftTransactionLock.AcquireAsync(db, accessor.Context.CompanyId,
                    accessor.Context.BranchId, draftId, cancellationToken))
                return Error(404, "DRAFT_NOT_FOUND");
            var checkout = await OwnedAsync(db, accessor, draftId, checkoutId, true, cancellationToken);
            var draft = await db.SaleDrafts.AsNoTracking().Include(item => item.Lines).SingleOrDefaultAsync(item =>
                item.Id == draftId && item.Status == SaleDraftStatus.Draft, cancellationToken);
            var shift = await OpenShiftAsync(db, accessor, cancellationToken);
            if (checkout is null || draft is null) return Error(404, "CHECKOUT_NOT_FOUND");
            if (shift is null || checkout.CashierShiftId != shift.Id ||
                checkout.Total != draft.TotalAfterDiscount ||
                checkout.DraftUpdatedAtUtc != draft.UpdatedAtUtc)
                return Error(409, "CHECKOUT_DRAFT_CHANGED");
            var termsError = await ValidateTermsAsync(checkout.Terms, checkout.Total,
                db, accessor, cancellationToken);
            if (termsError is not null) return termsError;
            try { checkout.Commit(request.ExpectedRevision, request.Key, clock.GetUtcNow()); }
            catch (InvalidOperationException) { return Error(409, "CHECKOUT_CONFLICT"); }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(checkout));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "CHECKOUT_CONFLICT");
        }
    }

    private static Task<IResult> ApproveAsync(Guid draftId, Guid checkoutId, Guid instrumentId,
        DecisionRequest? request, PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider clock, CancellationToken cancellationToken) =>
        DecideAsync(draftId, checkoutId, instrumentId, request, true, db, accessor, clock,
            cancellationToken);

    private static Task<IResult> RejectAsync(Guid draftId, Guid checkoutId, Guid instrumentId,
        DecisionRequest? request, PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider clock, CancellationToken cancellationToken) =>
        DecideAsync(draftId, checkoutId, instrumentId, request, false, db, accessor, clock,
            cancellationToken);

    private static async Task<IResult> DecideAsync(Guid draftId, Guid checkoutId, Guid instrumentId,
        DecisionRequest? request, bool approve, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request?.Reference?.Length > 200 || !approve && string.IsNullOrWhiteSpace(request?.Reference))
            return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            if (!await SaleDraftTransactionLock.AcquireAsync(db, accessor.Context.CompanyId,
                    accessor.Context.BranchId, draftId, cancellationToken))
                return Error(404, "DRAFT_NOT_FOUND");
            var checkout = await OwnedAsync(db, accessor, draftId, checkoutId, true, cancellationToken);
            if (checkout is null) return Error(404, "CHECKOUT_NOT_FOUND");
            if (await OpenShiftAsync(db, accessor, cancellationToken) is not { } shift ||
                shift.Id != checkout.CashierShiftId)
                return Error(409, "CASHIER_SHIFT_REQUIRED");
            try
            {
                if (approve) checkout.ApproveManual(instrumentId, request?.Reference, clock.GetUtcNow());
                else checkout.RejectManual(instrumentId, request!.Reference!, clock.GetUtcNow());
            }
            catch (Exception exception) when (exception is InvalidOperationException or
                PaymentSettlementException)
            {
                return Error(409, "CHECKOUT_OPERATION_CONFLICT");
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(checkout));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "CHECKOUT_CONFLICT");
        }
    }

    private static async Task<IResult> AddReplacementAsync(Guid draftId, Guid checkoutId,
        PaymentRequest? request, PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request is null || request.Id == Guid.Empty || !TryParseMethod(request.Method, out var method) ||
            request.Reference?.Length > 200 || request.Details is not null &&
            !request.Details.IsValidFor(method))
            return Error(400, "VALIDATION_ERROR");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            if (!await SaleDraftTransactionLock.AcquireAsync(db, accessor.Context.CompanyId,
                    accessor.Context.BranchId, draftId, cancellationToken))
                return Error(404, "DRAFT_NOT_FOUND");
            var checkout = await OwnedAsync(db, accessor, draftId, checkoutId, true, cancellationToken);
            if (checkout is null) return Error(404, "CHECKOUT_NOT_FOUND");
            if (await OpenShiftAsync(db, accessor, cancellationToken) is not { } shift ||
                shift.Id != checkout.CashierShiftId)
                return Error(409, "CASHIER_SHIFT_REQUIRED");
            try
            {
                checkout.AddReplacement(new SaleCheckoutPayment(request.Id, method,
                    request.Amount, request.Reference, request.Details), clock.GetUtcNow());
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            { return Error(409, "CHECKOUT_OPERATION_CONFLICT"); }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(checkout));
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Error(409, "CHECKOUT_CONFLICT");
        }
    }

    private static Task<SaleCheckout?> OwnedAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, Guid draftId, Guid? checkoutId, bool tracked,
        CancellationToken cancellationToken)
    {
        var query = db.SaleCheckouts.Include(item => item.Instruments).AsQueryable();
        if (!tracked) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(item => item.CompanyId == accessor.Context.CompanyId &&
            item.BranchId == accessor.Context.BranchId && item.SaleDraftId == draftId &&
            item.CashierId == accessor.Context.UserId &&
            item.PosTerminalId == accessor.TerminalId &&
            (checkoutId == null || item.Id == checkoutId), cancellationToken);
    }

    private static Task<CashierShift?> OpenShiftAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken) =>
        db.CashierShifts.SingleOrDefaultAsync(item =>
            item.CompanyId == accessor.Context.CompanyId && item.BranchId == accessor.Context.BranchId &&
            item.CashierId == accessor.Context.UserId && item.PosTerminalId == accessor.TerminalId &&
            item.Status == CashierShiftStatus.Open, cancellationToken);

    internal static object ToResponse(SaleCheckout checkout) => new
    {
        id = checkout.Id, saleDraftId = checkout.SaleDraftId, status = checkout.Status.ToString(),
        revision = checkout.Revision, total = checkout.Total, planHash = checkout.PlanHash,
        commitKey = checkout.CommitKey, approvedAmount = checkout.ApprovedAmount,
        remainingAmount = checkout.RemainingAmount, confirmedSaleId = checkout.ConfirmedSaleId,
        terms = checkout.Terms,
        instruments = checkout.Instruments.OrderBy(item => item.Id).Select(item => new
        {
            id = item.Id, method = MethodName(item.Method), amount = item.Amount,
            status = item.Status.ToString(), reference = item.Reference, details = item.Details,
            pointPaymentIntentId = item.PointPaymentIntentId, approvedAtUtc = item.ApprovedAtUtc
        }).ToArray()
    };

    private static bool ValidTerms(SaleCheckoutTerms terms) =>
        terms.DocumentType is { Length: > 0 and <= 32 } &&
        terms.RecipientTaxStatus is { Length: > 0 and <= 32 } &&
        terms.RecipientName is null or { Length: <= 200 } &&
        terms.RecipientDocumentNumber is null or { Length: <= 20 } &&
        terms.RecipientAddress is null or { Length: <= 200 } &&
        terms.RecipientLocality is null or { Length: <= 100 } &&
        terms.RecipientProvince is null or { Length: <= 100 } &&
        terms.PurchaseOrderNumber is null or { Length: <= 80 } &&
        terms.AccountChargeReference is null or { Length: <= 120 } &&
        (terms.AccountChargeAmount == 0 || terms.AccountChargeConfirmed) &&
        (terms.CustomerId is not null || terms.AccountChargeAmount == 0 &&
            terms.CreditAppliedAmount == 0) &&
        (terms.CustomerId is null || terms.AccountChargeAmount > 0 ||
            terms.CreditAppliedAmount > 0);

    private static async Task<IResult?> ValidateTermsAsync(SaleCheckoutTerms terms, decimal total,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        if (!ValidTerms(terms)) return Error(400, "VALIDATION_ERROR");
        SaleDocumentChoice document;
        try
        {
            document = SaleDocumentChoice.Create(terms.DocumentType, terms.RecipientTaxStatus,
                terms.RecipientName, terms.RecipientDocumentNumber, terms.RecipientAddress,
                total, terms.RecipientLocality, terms.RecipientProvince);
        }
        catch (ArgumentException) { return Error(400, "DOCUMENT_DETAILS_INVALID"); }
        if (terms.AccountChargeAmount > 0 || terms.CreditAppliedAmount > 0)
        {
            if (!accessor.Context.Permissions.Contains(PlatformPermissionCatalog.PosAccountCharge))
                return Error(403, "ACCOUNT_CHARGE_FORBIDDEN");
            var customer = await db.CustomerAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == accessor.Context.CompanyId && item.Id == terms.CustomerId,
                cancellationToken);
            if (customer?.CanChargeToAccount != true)
                return Error(409, "CUSTOMER_CREDIT_UNAVAILABLE");
            if (terms.CreditAppliedAmount > 0)
            {
                var received = await db.CustomerCollectionReceipts.AsNoTracking()
                    .Where(item => item.CompanyId == accessor.Context.CompanyId &&
                        item.CustomerId == terms.CustomerId && !item.IsVoided)
                    .SumAsync(item => (decimal?)item.CreditAmount, cancellationToken) ?? 0;
                var used = await db.CustomerCreditApplications.AsNoTracking()
                    .Where(item => item.CompanyId == accessor.Context.CompanyId &&
                        item.CustomerId == terms.CustomerId)
                    .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
                if (terms.CreditAppliedAmount > received - used)
                    return Error(409, "CUSTOMER_CREDIT_INSUFFICIENT");
            }
        }
        if (terms.FiscalCustomerId is Guid fiscalId)
        {
            if (document.Type == SaleDocumentType.NonFiscalTicket ||
                terms.CustomerId is Guid customerId && customerId != fiscalId)
                return Error(400, "FISCAL_CUSTOMER_MISMATCH");
            var fiscal = await db.CustomerAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == accessor.Context.CompanyId && item.Id == fiscalId &&
                item.IsActive && item.FiscalTaxStatus != null, cancellationToken);
            if (fiscal is null || fiscal.FiscalTaxStatus != document.RecipientTaxStatus ||
                fiscal.Name != document.RecipientName ||
                fiscal.FiscalDocumentNumber != document.RecipientDocumentNumber ||
                fiscal.FiscalAddress != document.RecipientAddress ||
                fiscal.FiscalLocality != document.RecipientLocality ||
                fiscal.FiscalProvince != document.RecipientProvince)
                return Error(409, "FISCAL_CUSTOMER_CHANGED");
        }
        return null;
    }

    private static bool TryParseMethod(string? value, out PaymentMethod method)
    {
        method = value?.Trim().ToLowerInvariant() switch
        {
            "cash" => PaymentMethod.Cash, "debit" => PaymentMethod.Debit,
            "credit" => PaymentMethod.Credit, "transfer" => PaymentMethod.Transfer,
            "mercadopago" => PaymentMethod.MercadoPago, "cheque" => PaymentMethod.Cheque,
            _ => (PaymentMethod)(-1)
        };
        return Enum.IsDefined(method);
    }

    private static PaymentMethod ParseMethod(string value) =>
        TryParseMethod(value, out var method) ? method : throw new ArgumentException("Payment method");

    private static string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "cash", PaymentMethod.Debit => "debit",
        PaymentMethod.Credit => "credit", PaymentMethod.Transfer => "transfer",
        PaymentMethod.MercadoPago => "mercadoPago", PaymentMethod.Cheque => "cheque",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
                return true;
        return false;
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar el cobro", [])),
        statusCode: status);

    private sealed record PrepareRequest(int ExpectedRevision, SaleCheckoutTerms? Terms,
        IReadOnlyList<PaymentRequest>? Payments);
    private sealed record PaymentRequest(Guid Id, string? Method, decimal Amount,
        string? Reference = null, SalePaymentInstrumentDetails? Details = null);
    private sealed record CommitRequest(int ExpectedRevision, Guid Key);
    private sealed record DecisionRequest(string? Reference);
}
