using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Customers;

public static class CustomerCollectionEndpoints
{
    public static IEndpointRouteBuilder MapCustomerCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/customers/{customerId:guid}/collections", CollectAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        endpoints.MapGet("/api/customers/collections/operations/{operationId:guid}", FindOperationAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        return endpoints;
    }

    private static async Task<IResult> FindOperationAsync(Guid operationId,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty) return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var context = accessor.Context;
        var receipt = await FindReceiptAsync(db, context.CompanyId, operationId, cancellationToken);
        if (receipt is null) return Results.NoContent();
        return receipt.BranchId == context.BranchId && receipt.CashierId == context.UserId &&
            receipt.PosTerminalId == accessor.TerminalId
            ? Results.Ok(ToResponse(receipt))
            : Results.NotFound();
    }

    private static async Task<IResult> CollectAsync(Guid customerId, CollectionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty || request is null || request.OperationId == Guid.Empty ||
            request.Amount <= 0 || request.Amount > 9_999_999_999.99m ||
            decimal.Round(request.Amount, 2) != request.Amount ||
            request.Allocations?.Count > 500 ||
            request.Allocations?.Any(item => item is null) == true ||
            request.Payments?.Count is > 6 or 0 ||
            request.Payments?.Any(item => item is null) == true ||
            (request.Payments is null) == string.IsNullOrWhiteSpace(request.Method))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var tenders = new List<PaymentTender>();
        if (request.Payments is null)
        {
            if (!TryParseMethod(request.Method, out var legacyMethod))
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
            tenders.Add(new PaymentTender(legacyMethod, request.Amount));
        }
        else
        {
            foreach (var payment in request.Payments)
            {
                if (!TryParseMethod(payment.Method, out var method))
                    return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
                tenders.Add(new PaymentTender(method, payment.Amount));
            }
        }
        PaymentSettlementResult settlement;
        try { settlement = PaymentSettlement.Calculate(request.Amount, tenders); }
        catch (PaymentSettlementException)
        { return Error(StatusCodes.Status400BadRequest, "PAYMENT_TOTAL_MISMATCH"); }
        var context = accessor.Context;
        if (accessor.TerminalId is null)
            return Error(StatusCodes.Status409Conflict, "POS_TERMINAL_REQUIRED");
        var requestHash = HashRequest(customerId, request, tenders);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var existing = await FindReceiptAsync(db, context.CompanyId, request.OperationId, cancellationToken);
            if (existing is not null)
                return ExistingResult(existing, requestHash, context.BranchId, context.UserId, accessor.TerminalId);

            var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.CashierId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
                item.Status == CashierShiftStatus.Open, cancellationToken);
            if (shift is null) return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_REQUIRED");

            var mercadoPagoAmount = settlement.AppliedPayments
                .Where(payment => payment.Method == PaymentMethod.MercadoPago)
                .Sum(payment => payment.AppliedAmount);
            PointPaymentIntent? mercadoPagoIntent = null;
            if (mercadoPagoAmount > 0)
            {
                if (request.MercadoPagoIntentId is not Guid intentId || intentId == Guid.Empty)
                    return Error(StatusCodes.Status409Conflict, "MERCADO_PAGO_APPROVAL_REQUIRED");
                mercadoPagoIntent = await db.PointPaymentIntents.SingleOrDefaultAsync(item =>
                    item.Id == intentId && item.CompanyId == context.CompanyId &&
                    item.BranchId == context.BranchId && item.CustomerId == customerId &&
                    item.CustomerCollectionOperationId == request.OperationId &&
                    item.CashierShiftId == shift.Id && item.CashierId == context.UserId &&
                    item.PosTerminalId == accessor.TerminalId && item.Amount == mercadoPagoAmount &&
                    item.Status == PointPaymentStatus.Approved &&
                    item.ConfirmedCollectionReceiptId == null &&
                    item.ProviderOrderId != null && item.ProviderPaymentId != null,
                    cancellationToken);
                if (mercadoPagoIntent is null)
                    return Error(StatusCodes.Status409Conflict, "MERCADO_PAGO_APPROVAL_REQUIRED");
            }
            else if (request.MercadoPagoIntentId is not null)
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
            if (mercadoPagoAmount == 0 && await db.PointPaymentIntents.AnyAsync(item =>
                item.CompanyId == context.CompanyId &&
                item.CustomerCollectionOperationId == request.OperationId &&
                (item.Status == PointPaymentStatus.Prepared || item.Status == PointPaymentStatus.Pending ||
                 item.Status == PointPaymentStatus.Approved ||
                 item.Status == PointPaymentStatus.NeedsReconciliation), cancellationToken))
                return Error(StatusCodes.Status409Conflict, "MERCADO_PAGO_ORDER_ACTIVE");

            var customer = await db.CustomerAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.Id == customerId, cancellationToken);
            if (customer is null) return Results.NotFound();

            var charges = await db.CustomerSaleCharges.AsNoTracking()
                .Where(item => item.CompanyId == context.CompanyId && item.CustomerId == customerId)
                .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
                .Select(item => new { item.SaleId, item.CreatedAtUtc, item.Amount })
                .ToArrayAsync(cancellationToken);
            var applied = await db.CustomerCollectionAllocations.AsNoTracking()
                .Where(item => item.CompanyId == context.CompanyId && item.CustomerId == customerId &&
                    !item.Receipt.IsVoided)
                .GroupBy(item => item.SaleId)
                .Select(group => new { SaleId = group.Key, Amount = group.Sum(item => item.Amount) })
                .ToDictionaryAsync(item => item.SaleId, item => item.Amount, cancellationToken);
            var outstanding = charges.Select(charge => new OutstandingAccountSale(charge.SaleId,
                charge.CreatedAtUtc, charge.Amount - applied.GetValueOrDefault(charge.SaleId))).ToArray();

            AccountCollectionPlanResult plan;
            try
            {
                plan = request.Allocations is null
                    ? CustomerCollectionPlan.Suggest(request.Amount, outstanding)
                    : CustomerCollectionPlan.Choose(request.Amount, outstanding,
                        request.Allocations.Select(item => new AccountAllocationChoice(item.SaleId, item.Amount))
                            .ToArray());
            }
            catch (AccountAllocationException)
            {
                return Error(StatusCodes.Status409Conflict, "ACCOUNT_ALLOCATION_CONFLICT");
            }

            var now = timeProvider.GetUtcNow();
            var receipt = new CustomerCollectionReceipt(context.CompanyId, context.BranchId,
                customerId, context.UserId, shift.Id, accessor.TerminalId, request.OperationId,
                requestHash, settlement.AppliedPayments[0].Method, request.Amount,
                plan.CreditAmount, now, payments: settlement.AppliedPayments);
            mercadoPagoIntent?.ApplyToCollection(receipt.Id);
            receipt.Allocations.AddRange(plan.Allocations.Select(item =>
                new CustomerCollectionAllocation(context.CompanyId, customerId, item.SaleId, item.Amount)));
            db.CustomerCollectionReceipts.Add(receipt);
            foreach (var payment in settlement.AppliedPayments)
                db.CashLedgerMovements.Add(new CashLedgerMovement(context.CompanyId, context.BranchId,
                    shift.Id, context.UserId, Guid.NewGuid(), payment.Method,
                    CashLedgerMovementKind.AccountCollection, payment.TenderedAmount, now,
                    posTerminalId: accessor.TerminalId));
            if (settlement.ChangeAmount > 0)
                db.CashLedgerMovements.Add(new CashLedgerMovement(context.CompanyId, context.BranchId,
                    shift.Id, context.UserId, Guid.NewGuid(), PaymentMethod.Cash,
                    CashLedgerMovementKind.AccountCollectionChange, -settlement.ChangeAmount, now,
                    posTerminalId: accessor.TerminalId));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Created($"/api/customers/{customerId}/collections/{receipt.Id}", ToResponse(receipt));
        }
        catch (Exception exception) when (IsConcurrentCollection(exception))
        {
            db.ChangeTracker.Clear();
            var existing = await FindReceiptAsync(db, context.CompanyId, request.OperationId, cancellationToken);
            return existing is null
                ? Error(StatusCodes.Status409Conflict, "ACCOUNT_COLLECTION_CONFLICT")
                : ExistingResult(existing, requestHash, context.BranchId, context.UserId, accessor.TerminalId);
        }
    }

    private static async Task<CustomerCollectionReceipt?> FindReceiptAsync(PlatformAccessDbContext db,
        Guid companyId, Guid operationId, CancellationToken cancellationToken) =>
        await db.CustomerCollectionReceipts.AsNoTracking()
            .Include(item => item.Allocations).Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.OperationId == operationId,
                cancellationToken);

    private static IResult ExistingResult(CustomerCollectionReceipt receipt, string requestHash,
        Guid branchId, Guid cashierId, Guid? terminalId) =>
        receipt.BranchId == branchId && receipt.CashierId == cashierId &&
        receipt.PosTerminalId == terminalId &&
        string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal)
            ? Results.Ok(ToResponse(receipt))
            : Error(StatusCodes.Status409Conflict, "IDEMPOTENCY_CONFLICT");

    private static CollectionResponse ToResponse(CustomerCollectionReceipt receipt) => new(
        receipt.Id, receipt.ReceiptNumber, receipt.CustomerId, receipt.Amount, receipt.CreditAmount,
        receipt.Payments.Count > 1 ? "mixed" : MethodName(receipt.Method), receipt.CreatedAtUtc,
        receipt.Allocations.OrderBy(item => item.SaleId)
            .Select(item => new CollectionAllocationResponse(item.SaleId, item.Amount)).ToArray(),
        receipt.Payments.Select(item => new CollectionPaymentResponse(
            MethodName(item.Method), item.TenderedAmount, item.AppliedAmount)).ToArray(),
        receipt.ChangeAmount);

    private static string HashRequest(Guid customerId, CollectionRequest request,
        List<PaymentTender> tenders)
    {
        var canonical = $"{customerId:N}|{(int)tenders[0].Method}|{request.Amount.ToString("0.00", CultureInfo.InvariantCulture)}";
        if (request.Payments is not null)
            canonical += "|payments" + string.Concat(tenders.OrderBy(item => item.Method)
                .Select(item => $"|{(int)item.Method}:{item.TenderedAmount.ToString("0.00", CultureInfo.InvariantCulture)}")) +
                $"|mercadoPago:{request.MercadoPagoIntentId:N}";
        canonical += request.Allocations is null ? "|automatic" : "|manual";
        if (request.Allocations is not null)
            canonical += string.Concat(request.Allocations.OrderBy(item => item.SaleId)
                .Select(item => $"|{item.SaleId:N}:{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool TryParseMethod(string? value, out PaymentMethod method)
    {
        method = value?.Trim().ToLowerInvariant() switch
        {
            "cash" => PaymentMethod.Cash,
            "debit" => PaymentMethod.Debit,
            "credit" => PaymentMethod.Credit,
            "transfer" => PaymentMethod.Transfer,
            "mercadopago" => PaymentMethod.MercadoPago,
            "cheque" => PaymentMethod.Cheque,
            _ => (PaymentMethod)(-1)
        };
        return Enum.IsDefined(method);
    }

    private static string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "cash",
        PaymentMethod.Debit => "debit",
        PaymentMethod.Credit => "credit",
        PaymentMethod.Transfer => "transfer",
        PaymentMethod.MercadoPago => "mercadoPago",
        PaymentMethod.Cheque => "cheque",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static bool IsConcurrentCollection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure })
                return true;
        return false;
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])), statusCode: statusCode);

    private sealed record CollectionRequest(Guid OperationId, string? Method, decimal Amount,
        IReadOnlyList<CollectionAllocationRequest>? Allocations = null,
        IReadOnlyList<CollectionPaymentRequest>? Payments = null, Guid? MercadoPagoIntentId = null);
    private sealed record CollectionAllocationRequest(Guid SaleId, decimal Amount);
    private sealed record CollectionPaymentRequest(string? Method, decimal Amount);
    private sealed record CollectionResponse(Guid Id, long ReceiptNumber, Guid CustomerId,
        decimal Amount, decimal CreditAmount, string Method, DateTimeOffset CreatedAtUtc,
        IReadOnlyList<CollectionAllocationResponse> Allocations,
        IReadOnlyList<CollectionPaymentResponse> Payments, decimal ChangeAmount);
    private sealed record CollectionAllocationResponse(Guid SaleId, decimal Amount);
    private sealed record CollectionPaymentResponse(string Method, decimal TenderedAmount, decimal AppliedAmount);
}
