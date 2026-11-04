using System.Data;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class CashWithdrawalEndpoints
{
    public static IEndpointRouteBuilder MapCashWithdrawalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cashier-shifts/current/withdrawals", ListCurrentAsync)
            .RequireOperationalContext();
        endpoints.MapPost("/api/cashier-shifts/current/withdrawals", RequestAsync)
            .RequireOperationalContext();
        var admin = endpoints.MapGroup("/api/admin/cash-withdrawals")
            .RequireOperationalPermission(PlatformPermissionCatalog.CashWithdrawalApprove);
        admin.MapGet("", ListAdminAsync);
        admin.MapPost("/{id:guid}/decision", DecideAsync);
        return endpoints;
    }

    private static async Task<IResult> ListCurrentAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
            item.CashierId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
            item.Status == CashierShiftStatus.Open, cancellationToken);
        if (shift is null) return Error(409, "CASHIER_SHIFT_REQUIRED");
        var rows = await db.CashWithdrawalRequests.AsNoTracking()
            .Where(item => item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId && item.CashierShiftId == shift.Id)
            .OrderByDescending(item => item.RequestedAtUtc).ThenByDescending(item => item.Id)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(rows.Select(item => ToResponse(item)).ToArray());
    }

    private static async Task<IResult> RequestAsync(CreateWithdrawalRequest? body,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider timeProvider, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ValidOrigin(httpContext)) return Error(403, "CSRF_REJECTED");
        if (body is null || body.OperationId == Guid.Empty || body.Method != "cash" ||
            body.Amount <= 0 || body.Amount > 9_999_999_999.99m ||
            decimal.Round(body.Amount, 2) != body.Amount ||
            string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length > 500)
            return Error(400, "VALIDATION_ERROR");
        if (accessor.TerminalId is null) return Error(409, "POS_TERMINAL_REQUIRED");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var existing = await db.CashWithdrawalRequests.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.OperationId == body.OperationId, cancellationToken);
            if (existing is not null)
                return SameRequest(existing, context.UserId, accessor.TerminalId, body);

            var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.CashierId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
                item.Status == CashierShiftStatus.Open, cancellationToken);
            if (shift is null) return Error(409, "CASHIER_SHIFT_REQUIRED");
            var request = new CashWithdrawalRequest(context.CompanyId, context.BranchId,
                accessor.TerminalId, shift.Id, context.UserId, body.OperationId,
                body.Amount, body.Reason, timeProvider.GetUtcNow());
            db.CashWithdrawalRequests.Add(request);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Created($"/api/cashier-shifts/current/withdrawals/{request.Id}",
                ToResponse(request));
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            db.ChangeTracker.Clear();
            var existing = await db.CashWithdrawalRequests.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.OperationId == body.OperationId, cancellationToken);
            return existing is null ? Error(409, "WITHDRAWAL_CONFLICT")
                : SameRequest(existing, context.UserId, accessor.TerminalId, body);
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        { return Error(409, "WITHDRAWAL_CONFLICT"); }
    }

    private static IResult SameRequest(CashWithdrawalRequest existing, Guid cashierId,
        Guid? terminalId, CreateWithdrawalRequest body) =>
        existing.RequestedByUserId == cashierId && existing.PosTerminalId == terminalId &&
        existing.Method == Carnicerias.Domain.Sales.PaymentMethod.Cash &&
        existing.Amount == body.Amount && existing.Reason == body.Reason.Trim()
            ? Results.Ok(ToResponse(existing)) : Error(409, "WITHDRAWAL_OPERATION_CONFLICT");

    private static async Task<IResult> ListAdminAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        Guid? branchId = null, string? status = null, int page = 1, int pageSize = 30)
    {
        var context = accessor.Context;
        if (branchId.HasValue && branchId != context.BranchId)
            return Error(403, "BRANCH_FORBIDDEN");
        if (page < 1 || pageSize is < 1 or > 100 ||
            !TryStatus(status, out var parsedStatus))
            return Error(400, "VALIDATION_ERROR");
        var query = db.CashWithdrawalRequests.AsNoTracking().Where(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
            (!parsedStatus.HasValue || item.Status == parsedStatus.Value));
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.RequestedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var userIds = rows.Select(item => item.RequestedByUserId)
            .Concat(rows.Where(item => item.DecidedByUserId.HasValue)
                .Select(item => item.DecidedByUserId!.Value)).Distinct().ToArray();
        var names = await db.Users.AsNoTracking()
            .Where(user => user.CompanyId == context.CompanyId && userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Username, cancellationToken);
        return Results.Ok(new
        {
            items = rows.Select(item => ToResponse(item,
                names.GetValueOrDefault(item.RequestedByUserId),
                item.DecidedByUserId.HasValue
                    ? names.GetValueOrDefault(item.DecidedByUserId.Value) : null)).ToArray(),
            page, pageSize, totalItems = total
        });
    }

    private static async Task<IResult> DecideAsync(Guid id, DecideWithdrawalRequest? body,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider timeProvider, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ValidOrigin(httpContext)) return Error(403, "CSRF_REJECTED");
        if (body is null || body.Action is not ("approve" or "reject") ||
            (body.Action == "reject" &&
             (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length > 500)))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var request = await db.CashWithdrawalRequests.SingleOrDefaultAsync(item =>
                item.Id == id && item.CompanyId == context.CompanyId &&
                item.BranchId == context.BranchId, cancellationToken);
            if (request is null) return Error(404, "WITHDRAWAL_NOT_FOUND");
            if (request.RequestedByUserId == context.UserId)
                return Error(403, "WITHDRAWAL_SELF_DECISION_FORBIDDEN");
            if (request.Status != CashWithdrawalStatus.Pending)
            {
                var sameDecision = body.Action == "approve" &&
                    request.Status == CashWithdrawalStatus.Approved ||
                    body.Action == "reject" && request.Status == CashWithdrawalStatus.Rejected &&
                    request.DecisionReason == body.Reason?.Trim();
                return sameDecision ? Results.Ok(ToResponse(request))
                    : Error(409, "WITHDRAWAL_ALREADY_DECIDED");
            }

            var now = timeProvider.GetUtcNow();
            if (body.Action == "reject")
            {
                request.Reject(context.UserId, body.Reason!, now);
            }
            else
            {
                var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.Id == request.CashierShiftId && item.CompanyId == context.CompanyId &&
                    item.BranchId == context.BranchId && item.CashierId == request.RequestedByUserId &&
                    item.PosTerminalId == request.PosTerminalId &&
                    item.Status == CashierShiftStatus.Open, cancellationToken);
                if (shift is null) return Error(409, "CASHIER_SHIFT_REQUIRED");
                var availableCash = await db.CashLedgerMovements.AsNoTracking()
                    .Where(item => item.CompanyId == context.CompanyId &&
                        item.BranchId == context.BranchId &&
                        item.CashierShiftId == shift.Id &&
                        item.Method == Carnicerias.Domain.Sales.PaymentMethod.Cash)
                    .SumAsync(item => (decimal?)item.AmountDelta, cancellationToken) ?? 0m;
                if (availableCash < request.Amount)
                    return Error(409, "WITHDRAWAL_INSUFFICIENT_CASH");
                var movement = new CashLedgerMovement(request.CompanyId, request.BranchId,
                    request.CashierShiftId, request.RequestedByUserId, request.OperationId,
                    Carnicerias.Domain.Sales.PaymentMethod.Cash,
                    CashLedgerMovementKind.ExtraordinaryWithdrawal, -request.Amount, now,
                    posTerminalId: request.PosTerminalId);
                request.Approve(context.UserId, movement.Id, availableCash, now);
                db.CashLedgerMovements.Add(movement);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(request));
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        { return Error(409, "WITHDRAWAL_CONFLICT"); }
        catch (Exception exception) when (IsSerializationFailure(exception))
        { return Error(409, "WITHDRAWAL_CONFLICT"); }
    }

    private static bool TryStatus(string? value, out CashWithdrawalStatus? status)
    {
        status = value switch
        {
            null or "" => null,
            "pending" => CashWithdrawalStatus.Pending,
            "approved" => CashWithdrawalStatus.Approved,
            "rejected" => CashWithdrawalStatus.Rejected,
            _ => null
        };
        return string.IsNullOrEmpty(value) || status.HasValue;
    }

    private static object ToResponse(CashWithdrawalRequest request,
        string? requesterName = null, string? decisionByName = null) => new
    {
        request.Id,
        request.OperationId,
        request.CashierShiftId,
        method = "cash",
        request.Amount,
        request.Reason,
        status = request.Status.ToString().ToLowerInvariant(),
        request.RequestedByUserId,
        requesterName,
        request.RequestedAtUtc,
        request.DecidedByUserId,
        decisionByName,
        request.DecidedAtUtc,
        request.DecisionReason,
        request.LedgerMovementId
    };

    private static bool ValidOrigin(HttpContext httpContext) =>
        string.IsNullOrEmpty(httpContext.Request.Headers.Origin.ToString()) ||
        RequestOriginValidator.IsAllowed(httpContext.Request.Headers.Origin.ToString(),
            httpContext.Request);

    private static bool IsUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
                return true;
        return false;
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: status);

    private sealed record CreateWithdrawalRequest(Guid OperationId, string Method,
        decimal Amount, string Reason);
    private sealed record DecideWithdrawalRequest(string Action, string? Reason);
}
