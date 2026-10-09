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
            !TryParseMethod(request.Method, out var method))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = accessor.Context;
        if (accessor.TerminalId is null)
            return Error(StatusCodes.Status409Conflict, "POS_TERMINAL_REQUIRED");
        var requestHash = HashRequest(customerId, request, method);
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

            var customer = await db.CustomerAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.Id == customerId, cancellationToken);
            if (customer is null) return Results.NotFound();

            var charges = await db.CustomerSaleCharges.AsNoTracking()
                .Where(item => item.CompanyId == context.CompanyId && item.CustomerId == customerId)
                .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
                .Select(item => new { item.SaleId, item.CreatedAtUtc, item.Amount })
                .ToArrayAsync(cancellationToken);
            var applied = await db.CustomerCollectionAllocations.AsNoTracking()
                .Where(item => item.CompanyId == context.CompanyId && item.CustomerId == customerId)
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
                requestHash, method, request.Amount, plan.CreditAmount, now);
            receipt.Allocations.AddRange(plan.Allocations.Select(item =>
                new CustomerCollectionAllocation(context.CompanyId, customerId, item.SaleId, item.Amount)));
            db.CustomerCollectionReceipts.Add(receipt);
            db.CashLedgerMovements.Add(new CashLedgerMovement(context.CompanyId, context.BranchId,
                shift.Id, context.UserId, request.OperationId, method,
                CashLedgerMovementKind.AccountCollection, request.Amount, now,
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
        await db.CustomerCollectionReceipts.AsNoTracking().Include(item => item.Allocations)
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
        MethodName(receipt.Method), receipt.CreatedAtUtc,
        receipt.Allocations.OrderBy(item => item.SaleId)
            .Select(item => new CollectionAllocationResponse(item.SaleId, item.Amount)).ToArray());

    private static string HashRequest(Guid customerId, CollectionRequest request, PaymentMethod method)
    {
        var canonical = $"{customerId:N}|{(int)method}|{request.Amount.ToString("0.00", CultureInfo.InvariantCulture)}";
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
        IReadOnlyList<CollectionAllocationRequest>? Allocations = null);
    private sealed record CollectionAllocationRequest(Guid SaleId, decimal Amount);
    private sealed record CollectionResponse(Guid Id, long ReceiptNumber, Guid CustomerId,
        decimal Amount, decimal CreditAmount, string Method, DateTimeOffset CreatedAtUtc,
        IReadOnlyList<CollectionAllocationResponse> Allocations);
    private sealed record CollectionAllocationResponse(Guid SaleId, decimal Amount);
}
