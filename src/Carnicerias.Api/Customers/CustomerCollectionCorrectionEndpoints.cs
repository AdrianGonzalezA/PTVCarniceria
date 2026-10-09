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

public static class CustomerCollectionCorrectionEndpoints
{
    public static IEndpointRouteBuilder MapCustomerCollectionCorrectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/customers/collections/{receiptId:guid}/corrections", CorrectAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCorrect);
        return endpoints;
    }

    private static async Task<IResult> CorrectAsync(Guid receiptId, CorrectionRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (receiptId == Guid.Empty || request is null || request.OperationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 240 ||
            request.Allocations?.Count > 500 || request.Allocations?.Any(item => item is null) == true ||
            !TryParseKind(request.Kind, out var kind) ||
            (kind == CustomerCollectionCorrectionKind.Refund &&
                (request.TargetCustomerId is not null || request.Allocations is not null)) ||
            (kind == CustomerCollectionCorrectionKind.Reallocate &&
                (request.TargetCustomerId is null || request.TargetCustomerId == Guid.Empty)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = accessor.Context;
        if (accessor.TerminalId is null)
            return Error(StatusCodes.Status409Conflict, "POS_TERMINAL_REQUIRED");
        var requestHash = HashRequest(receiptId, request, kind);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var existing = await db.CustomerCollectionCorrections.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                    item.OperationId == request.OperationId, cancellationToken);
            if (existing is not null)
                return await ExistingResultAsync(db, existing, requestHash, context.BranchId,
                    context.UserId, accessor.TerminalId, cancellationToken);

            var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.CashierId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
                item.Status == CashierShiftStatus.Open, cancellationToken);
            if (shift is null) return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_REQUIRED");

            var original = await db.CustomerCollectionReceipts.Include(item => item.Allocations)
                .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId && item.Id == receiptId,
                    cancellationToken);
            if (original is null) return Results.NotFound();
            if (original.IsVoided) return Error(StatusCodes.Status409Conflict, "COLLECTION_ALREADY_CORRECTED");

            var availableCredit = await db.CustomerCollectionReceipts.AsNoTracking()
                .Where(item => item.CompanyId == context.CompanyId &&
                    item.CustomerId == original.CustomerId && !item.IsVoided)
                .SumAsync(item => (decimal?)item.CreditAmount, cancellationToken) ?? 0;
            availableCredit -= await db.CustomerCreditApplications.AsNoTracking()
                .Where(item => item.CompanyId == context.CompanyId && item.CustomerId == original.CustomerId)
                .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
            if (availableCredit < original.CreditAmount)
                return Error(StatusCodes.Status409Conflict, "COLLECTION_CREDIT_ALREADY_USED");

            var now = timeProvider.GetUtcNow();
            CustomerCollectionReceipt? replacement = null;
            if (kind == CustomerCollectionCorrectionKind.Reallocate)
            {
                var targetCustomerId = request.TargetCustomerId!.Value;
                if (!await db.CustomerAccounts.AsNoTracking().AnyAsync(item =>
                        item.CompanyId == context.CompanyId && item.Id == targetCustomerId,
                        cancellationToken)) return Error(StatusCodes.Status404NotFound, "CUSTOMER_NOT_FOUND");
                var charges = await db.CustomerSaleCharges.AsNoTracking().Where(item =>
                        item.CompanyId == context.CompanyId && item.CustomerId == targetCustomerId)
                    .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
                    .Select(item => new { item.SaleId, item.CreatedAtUtc, item.Amount })
                    .ToArrayAsync(cancellationToken);
                var applied = await db.CustomerCollectionAllocations.AsNoTracking()
                    .Where(item => item.CompanyId == context.CompanyId &&
                        item.CustomerId == targetCustomerId && !item.Receipt.IsVoided &&
                        item.ReceiptId != original.Id)
                    .GroupBy(item => item.SaleId)
                    .Select(group => new { SaleId = group.Key, Amount = group.Sum(item => item.Amount) })
                    .ToDictionaryAsync(item => item.SaleId, item => item.Amount, cancellationToken);
                var outstanding = charges.Select(charge => new OutstandingAccountSale(charge.SaleId,
                    charge.CreatedAtUtc, charge.Amount - applied.GetValueOrDefault(charge.SaleId))).ToArray();
                AccountCollectionPlanResult plan;
                try
                {
                    plan = request.Allocations is null
                        ? CustomerCollectionPlan.Suggest(original.Amount, outstanding)
                        : CustomerCollectionPlan.Choose(original.Amount, outstanding,
                            request.Allocations.Select(item => new AccountAllocationChoice(item.SaleId, item.Amount))
                                .ToArray());
                }
                catch (AccountAllocationException)
                {
                    return Error(StatusCodes.Status409Conflict, "ACCOUNT_ALLOCATION_CONFLICT");
                }
                replacement = new CustomerCollectionReceipt(context.CompanyId, context.BranchId,
                    targetCustomerId, context.UserId, shift.Id, accessor.TerminalId, Guid.NewGuid(),
                    requestHash, original.Method, original.Amount, plan.CreditAmount, now,
                    CollectionReceiptOrigin.Reallocation, original.Id);
                replacement.Allocations.AddRange(plan.Allocations.Select(item =>
                    new CustomerCollectionAllocation(context.CompanyId, targetCustomerId,
                        item.SaleId, item.Amount)));
                db.CustomerCollectionReceipts.Add(replacement);
            }
            else
            {
                db.CashLedgerMovements.Add(new CashLedgerMovement(context.CompanyId, context.BranchId,
                    shift.Id, context.UserId, request.OperationId, original.Method,
                    CashLedgerMovementKind.AccountCollectionRefund, -original.Amount, now,
                    posTerminalId: accessor.TerminalId));
            }

            original.Void(now);
            var correction = new CustomerCollectionCorrection(context.CompanyId, context.BranchId,
                original.Id, replacement?.Id, context.UserId, shift.Id, accessor.TerminalId,
                request.OperationId, requestHash, kind, request.Reason, original.Amount, now);
            db.CustomerCollectionCorrections.Add(correction);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Created($"/api/customers/collections/{original.Id}/corrections/{correction.Id}",
                ToResponse(correction, replacement));
        }
        catch (Exception exception) when (IsConcurrentCorrection(exception))
        {
            db.ChangeTracker.Clear();
            var existing = await db.CustomerCollectionCorrections.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                    item.OperationId == request.OperationId, cancellationToken);
            return existing is null ? Error(StatusCodes.Status409Conflict, "COLLECTION_CORRECTION_CONFLICT")
                : await ExistingResultAsync(db, existing, requestHash, context.BranchId,
                    context.UserId, accessor.TerminalId, cancellationToken);
        }
    }

    private static async Task<IResult> ExistingResultAsync(PlatformAccessDbContext db,
        CustomerCollectionCorrection correction, string requestHash, Guid branchId, Guid cashierId,
        Guid? terminalId, CancellationToken cancellationToken)
    {
        if (correction.BranchId != branchId || correction.CashierId != cashierId ||
            correction.PosTerminalId != terminalId ||
            !string.Equals(correction.RequestHash, requestHash, StringComparison.Ordinal))
            return Error(StatusCodes.Status409Conflict, "IDEMPOTENCY_CONFLICT");
        var replacement = correction.ReplacementReceiptId is Guid replacementId
            ? await db.CustomerCollectionReceipts.AsNoTracking().Include(item => item.Allocations)
                .SingleAsync(item => item.CompanyId == correction.CompanyId && item.Id == replacementId,
                    cancellationToken)
            : null;
        return Results.Ok(ToResponse(correction, replacement));
    }

    private static CorrectionResponse ToResponse(CustomerCollectionCorrection correction,
        CustomerCollectionReceipt? replacement) => new(correction.Id, correction.CorrectionNumber,
        correction.OriginalReceiptId, correction.ReplacementReceiptId,
        correction.Kind == CustomerCollectionCorrectionKind.Refund ? "refund" : "reallocate",
        correction.Reason, correction.Amount, correction.CreatedAtUtc,
        replacement?.ReceiptNumber, replacement?.CustomerId, replacement?.CreditAmount,
        replacement?.Allocations.Select(item => new AllocationResponse(item.SaleId, item.Amount)).ToArray());

    private static string HashRequest(Guid receiptId, CorrectionRequest request,
        CustomerCollectionCorrectionKind kind)
    {
        var canonical = $"{receiptId:N}|{(int)kind}|{request.Reason!.Trim()}|{request.TargetCustomerId:N}";
        canonical += request.Allocations is null ? "|automatic" : "|manual";
        if (request.Allocations is not null)
            canonical += string.Concat(request.Allocations.OrderBy(item => item.SaleId)
                .Select(item => $"|{item.SaleId:N}:{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool TryParseKind(string? value, out CustomerCollectionCorrectionKind kind)
    {
        kind = value?.Trim().ToLowerInvariant() switch
        {
            "reallocate" => CustomerCollectionCorrectionKind.Reallocate,
            "refund" => CustomerCollectionCorrectionKind.Refund,
            _ => (CustomerCollectionCorrectionKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static bool IsConcurrentCorrection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure })
                return true;
        return false;
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])), statusCode: statusCode);

    private sealed record CorrectionRequest(Guid OperationId, string? Kind, string? Reason,
        Guid? TargetCustomerId = null, IReadOnlyList<AllocationRequest>? Allocations = null);
    private sealed record AllocationRequest(Guid SaleId, decimal Amount);
    private sealed record AllocationResponse(Guid SaleId, decimal Amount);
    private sealed record CorrectionResponse(Guid Id, long CorrectionNumber, Guid OriginalReceiptId,
        Guid? ReplacementReceiptId, string Kind, string Reason, decimal Amount, DateTimeOffset CreatedAtUtc,
        long? ReplacementReceiptNumber, Guid? CustomerId, decimal? CreditAmount,
        IReadOnlyList<AllocationResponse>? Allocations);
}
