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

namespace Carnicerias.Api.Sales;

public static class SaleConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapSaleConfirmationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sales/drafts/{draftId:guid}/confirmation", ConfirmAsync)
            .RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> ConfirmAsync(
        Guid draftId,
        ConfirmSaleRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (draftId == Guid.Empty || request?.Payments is null || request.Payments.Count > 6 ||
            request.Payments.Any(payment => payment is null) || request.CustomerId == Guid.Empty ||
            (request.CustomerId is not null && request.AccountChargeAmount <= 0 && request.CreditAppliedAmount <= 0) ||
            (request.CustomerId is null && (request.AccountChargeAmount != 0 || request.CreditAppliedAmount != 0)) ||
            request.AccountChargeAmount < 0 || request.CreditAppliedAmount < 0)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var tenders = new List<PaymentTender>(request.Payments.Count);
        foreach (var payment in request.Payments)
        {
            if (!TryParseMethod(payment.Method, out var method))
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
            tenders.Add(new PaymentTender(method, payment.Amount));
        }

        var requestHash = HashPaymentRequest(tenders, request.CustomerId, request.AccountChargeAmount,
            request.CreditAppliedAmount);
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var existingSale = await FindSaleAsync(db, context.CompanyId, context.BranchId,
                context.UserId, accessor.TerminalId, draftId, cancellationToken);
            if (existingSale is not null)
                return ExistingSaleResult(existingSale, requestHash);

            var draft = await db.SaleDrafts.Include(item => item.Lines).SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.UserId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
                item.Id == draftId && item.Status == SaleDraftStatus.Draft,
                cancellationToken);
            if (draft is null || draft.Lines.Count == 0)
                return Error(StatusCodes.Status409Conflict, "SALE_NOT_CONFIRMABLE");

            var shift = await db.CashierShifts.SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.CashierId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
                item.Status == CashierShiftStatus.Open,
                cancellationToken);
            if (shift is null) return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_REQUIRED");
            if (accessor.TerminalId is not null && draft.CashierShiftId != shift.Id)
                return Error(StatusCodes.Status409Conflict, "SALE_NOT_CONFIRMABLE");

            var total = draft.Lines.Sum(line => decimal.Round(
                line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero));
            CustomerAccount? creditCustomer = null;
            if (request.AccountChargeAmount > 0 || request.CreditAppliedAmount > 0)
            {
                if (!context.Permissions.Contains(PlatformPermissionCatalog.PosAccountCharge))
                    return Error(StatusCodes.Status403Forbidden, "ACCOUNT_CHARGE_FORBIDDEN");
                if (request.AccountChargeAmount > 0 && !request.AccountChargeConfirmed)
                    return Error(StatusCodes.Status400BadRequest, "ACCOUNT_CHARGE_CONFIRMATION_REQUIRED");
                creditCustomer = await db.CustomerAccounts.SingleOrDefaultAsync(customer =>
                    customer.CompanyId == context.CompanyId && customer.Id == request.CustomerId,
                    cancellationToken);
                if (creditCustomer?.CanChargeToAccount != true)
                    return Error(StatusCodes.Status409Conflict, "CUSTOMER_CREDIT_UNAVAILABLE");
                if (request.CreditAppliedAmount > 0)
                {
                    var receivedCredit = await db.CustomerCollectionReceipts.AsNoTracking()
                        .Where(item => item.CompanyId == context.CompanyId && item.CustomerId == request.CustomerId)
                        .SumAsync(item => (decimal?)item.CreditAmount, cancellationToken) ?? 0;
                    var usedCredit = await db.CustomerCreditApplications.AsNoTracking()
                        .Where(item => item.CompanyId == context.CompanyId && item.CustomerId == request.CustomerId)
                        .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
                    if (request.CreditAppliedAmount > receivedCredit - usedCredit)
                        return Error(StatusCodes.Status409Conflict, "CUSTOMER_CREDIT_INSUFFICIENT");
                }
            }
            PaymentSettlementResult settlement;
            try
            {
                settlement = request.AccountChargeAmount > 0 || request.CreditAppliedAmount > 0
                    ? PaymentSettlement.CalculateWithAccountCharge(total, tenders,
                        request.AccountChargeAmount, request.CreditAppliedAmount)
                    : PaymentSettlement.Calculate(total, tenders);
            }
            catch (PaymentSettlementException exception)
            {
                return PaymentError(exception.Error);
            }

            var now = timeProvider.GetUtcNow();
            foreach (var pieceId in draft.Lines.Where(line => line.InventoryPieceId is not null)
                         .Select(line => line.InventoryPieceId!.Value))
                if (await db.ConfirmedSaleLines.AnyAsync(line => line.InventoryPieceId == pieceId, cancellationToken))
                    return Error(StatusCodes.Status409Conflict, "PIECE_ALREADY_USED");
            var sale = new ConfirmedSale(context.CompanyId, context.BranchId, context.UserId,
                shift.Id, draft.Id, draft.PriceListId, total, requestHash, now, accessor.TerminalId,
                request.CustomerId, request.AccountChargeAmount, creditCustomer?.Code, creditCustomer?.Name,
                request.CreditAppliedAmount);
            sale.Lines.AddRange(draft.Lines.Select(line => new ConfirmedSaleLine(
                context.CompanyId, line.ProductId, line.ProductCode, line.ProductName, line.Unit,
                line.SaleMode, line.Quantity, line.UnitPrice, line.InventoryPieceId, line.PieceIdentifier)));
            sale.Payments.AddRange(settlement.AppliedPayments.Select(payment => new SalePayment(
                payment.Method, payment.TenderedAmount, payment.AppliedAmount)));
            db.ConfirmedSales.Add(sale);
            if (request.CustomerId is Guid customerId && request.AccountChargeAmount > 0)
                db.CustomerSaleCharges.Add(new CustomerSaleCharge(context.CompanyId, context.BranchId,
                    customerId, sale.Id, context.UserId, shift.Id, request.AccountChargeAmount, now));
            if (request.CustomerId is Guid creditCustomerId && request.CreditAppliedAmount > 0)
                db.CustomerCreditApplications.Add(new CustomerCreditApplication(context.CompanyId,
                    context.BranchId, creditCustomerId, sale.Id, context.UserId, shift.Id,
                    request.CreditAppliedAmount, now));

            foreach (var group in draft.Lines.GroupBy(line => line.ProductId))
            {
                var quantity = group.Sum(line => line.Quantity);
                var changed = await db.BranchInventoryBalances.Where(balance =>
                        balance.CompanyId == context.CompanyId && balance.BranchId == context.BranchId &&
                        balance.ProductId == group.Key && balance.OnHand >= quantity &&
                        balance.Reserved >= quantity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(balance => balance.OnHand, balance => balance.OnHand - quantity)
                        .SetProperty(balance => balance.Reserved, balance => balance.Reserved - quantity),
                        cancellationToken);
                if (changed == 0)
                    return Error(StatusCodes.Status409Conflict, "STOCK_RESERVATION_MISSING");

                db.InventoryMovements.Add(new InventoryMovement(
                    context.CompanyId, context.BranchId, group.Key, context.UserId,
                    Guid.NewGuid(), InventoryMovementKind.Sale, -quantity,
                    $"Egreso por venta {sale.Id:N}", now));
            }

            foreach (var payment in settlement.AppliedPayments)
            {
                var amount = payment.Method == PaymentMethod.Cash
                    ? payment.TenderedAmount
                    : payment.AppliedAmount;
                db.CashLedgerMovements.Add(new CashLedgerMovement(
                    context.CompanyId, context.BranchId, shift.Id, context.UserId,
                    Guid.NewGuid(), payment.Method, CashLedgerMovementKind.SalePayment,
                    amount, now, sale.Id, accessor.TerminalId));
            }
            if (settlement.ChangeAmount > 0)
                db.CashLedgerMovements.Add(new CashLedgerMovement(
                    context.CompanyId, context.BranchId, shift.Id, context.UserId,
                    Guid.NewGuid(), PaymentMethod.Cash, CashLedgerMovementKind.Change,
                    -settlement.ChangeAmount, now, sale.Id, accessor.TerminalId));

            draft.Confirm(sale.Id, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Created($"/api/sales/{sale.Id}", ToResponse(sale, settlement.ChangeAmount));
        }
        catch (Exception exception) when (IsConcurrentConfirmation(exception))
        {
            db.ChangeTracker.Clear();
            var existingSale = await FindSaleAsync(db, context.CompanyId, context.BranchId,
                context.UserId, accessor.TerminalId, draftId, cancellationToken);
            return existingSale is null
                ? Error(StatusCodes.Status409Conflict, "SALE_CONFIRMATION_CONFLICT")
                : ExistingSaleResult(existingSale, requestHash);
        }
    }

    private static async Task<ConfirmedSale?> FindSaleAsync(
        PlatformAccessDbContext db, Guid companyId, Guid branchId, Guid cashierId, Guid? terminalId,
        Guid draftId, CancellationToken cancellationToken) =>
        await db.ConfirmedSales.AsNoTracking().Include(sale => sale.Lines).Include(sale => sale.Payments)
            .SingleOrDefaultAsync(sale => sale.CompanyId == companyId && sale.BranchId == branchId &&
                sale.CashierId == cashierId && sale.PosTerminalId == terminalId &&
                sale.SourceDraftId == draftId,
                cancellationToken);

    private static IResult ExistingSaleResult(ConfirmedSale sale, string requestHash) =>
        string.Equals(sale.PaymentRequestHash, requestHash, StringComparison.Ordinal)
            ? Results.Ok(ToResponse(sale, sale.Payments.Where(payment => payment.Method == PaymentMethod.Cash)
                .Sum(payment => payment.TenderedAmount - payment.AppliedAmount)))
            : Error(StatusCodes.Status409Conflict, "IDEMPOTENCY_CONFLICT");

    private static string HashPaymentRequest(IEnumerable<PaymentTender> tenders, Guid? customerId,
        decimal accountCharge, decimal creditApplied)
    {
        var canonical = string.Join('|', tenders.OrderBy(tender => tender.Method)
            .Select(tender => $"{(int)tender.Method}:{tender.TenderedAmount.ToString("0.00", CultureInfo.InvariantCulture)}"));
        if (customerId is not null)
        {
            canonical += $"|account:{customerId.Value:N}:{accountCharge.ToString("0.00", CultureInfo.InvariantCulture)}";
            if (creditApplied > 0)
                canonical += $":{creditApplied.ToString("0.00", CultureInfo.InvariantCulture)}";
        }
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

    private static bool IsConcurrentConfirmation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure })
                return true;
        return false;
    }

    private static SaleConfirmationResponse ToResponse(ConfirmedSale sale, decimal change) => new(
        sale.Id, sale.Total, change, sale.ConfirmedAtUtc, sale.CustomerId, sale.AccountChargeAmount,
        sale.CreditAppliedAmount,
        sale.CustomerCode, sale.CustomerName,
        sale.Lines.OrderBy(line => line.ProductName).ThenBy(line => line.PieceIdentifier)
            .Select(line => new SaleConfirmationLineResponse(
                line.ProductCode, line.ProductName, line.Unit, line.Quantity, line.UnitPrice,
                line.LineTotal, line.PieceIdentifier)).ToArray(),
        sale.Payments.Select(payment => new SaleConfirmationPaymentResponse(
            MethodName(payment.Method), payment.TenderedAmount, payment.AppliedAmount)).ToArray());

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

    private static IResult PaymentError(PaymentSettlementError error) => error switch
    {
        PaymentSettlementError.AmountPending => Error(StatusCodes.Status400BadRequest, "PAYMENT_TOTAL_MISMATCH"),
        PaymentSettlementError.NonCashOverpayment => Error(StatusCodes.Status400BadRequest, "INVALID_CHANGE"),
        PaymentSettlementError.DuplicateMethod => Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR"),
        _ => Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR")
    };

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])), statusCode: statusCode);

    private sealed record ConfirmSaleRequest(IReadOnlyList<PaymentRequest>? Payments, Guid? CustomerId = null,
        decimal AccountChargeAmount = 0, bool AccountChargeConfirmed = false,
        decimal CreditAppliedAmount = 0);
    private sealed record PaymentRequest(string? Method, decimal Amount);
    private sealed record SaleConfirmationResponse(Guid Id, decimal Total, decimal ChangeAmount,
        DateTimeOffset ConfirmedAtUtc, Guid? CustomerId, decimal AccountChargeAmount,
        decimal CreditAppliedAmount,
        string? CustomerCode, string? CustomerName,
        IReadOnlyList<SaleConfirmationLineResponse> Lines,
        IReadOnlyList<SaleConfirmationPaymentResponse> Payments);
    private sealed record SaleConfirmationLineResponse(string Code, string Name, string Unit,
        decimal Quantity, decimal UnitPrice, decimal LineTotal, string? PieceIdentifier);
    private sealed record SaleConfirmationPaymentResponse(string Method, decimal TenderedAmount, decimal AppliedAmount);
}
