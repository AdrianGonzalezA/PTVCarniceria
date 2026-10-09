using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Carnicerias.Api.Sessions;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class CashierShiftEndpoints
{
    public static IEndpointRouteBuilder MapCashierShiftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cashier-shifts/current", GetCurrentAsync).RequireOperationalContext();
        endpoints.MapGet("/api/cashier-shifts/last-closed", GetLastClosedAsync).RequireOperationalContext();
        endpoints.MapPost("/api/cashier-shifts", OpenAsync).RequireOperationalContext();
        endpoints.MapPost("/api/cashier-shifts/current/close", CloseAsync).RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> GetCurrentAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
            item.CashierId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
            item.Status == CashierShiftStatus.Open,
            cancellationToken);

        return shift is null ? Results.NoContent() : Results.Ok(await ToResponseAsync(db, shift, cancellationToken));
    }

    private static async Task<IResult> GetLastClosedAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var shift = await db.CashierShifts.AsNoTracking()
            .Where(item => item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.CashierId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
                item.Status == CashierShiftStatus.Closed)
            .OrderByDescending(item => item.ClosedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return shift is null ? Results.NoContent() : Results.Ok(await ToResponseAsync(db, shift, cancellationToken));
    }

    private static async Task<IResult> OpenAsync(
        OpenShiftRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request is null || request.OpeningCash < 0 || request.OpeningCash > 9_999_999_999.99m ||
            decimal.Round(request.OpeningCash, 2) != request.OpeningCash)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var context = accessor.Context;
        var shift = new CashierShift(context.CompanyId, context.BranchId, context.UserId,
            request.OpeningCash, timeProvider.GetUtcNow(), accessor.TerminalId);
        db.CashierShifts.Add(shift);
        if (request.OpeningCash > 0)
            db.CashLedgerMovements.Add(new CashLedgerMovement(
                context.CompanyId, context.BranchId, shift.Id, context.UserId,
                shift.Id, PaymentMethod.Cash, CashLedgerMovementKind.Opening,
                request.OpeningCash, shift.OpenedAtUtc, posTerminalId: accessor.TerminalId));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_ALREADY_OPEN");
        }

        return Results.Created("/api/cashier-shifts/current", await ToResponseAsync(db, shift, cancellationToken));
    }

    private static async Task<IResult> CloseAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var shift = await db.CashierShifts.SingleOrDefaultAsync(item =>
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.CashierId == context.UserId && item.PosTerminalId == accessor.TerminalId &&
                item.Status == CashierShiftStatus.Open,
                cancellationToken);
            if (shift is null) return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_REQUIRED");

            var hasDraft = await db.SaleDrafts.AnyAsync(draft =>
                draft.CompanyId == context.CompanyId && draft.BranchId == context.BranchId &&
                draft.UserId == context.UserId && draft.PosTerminalId == accessor.TerminalId &&
                (accessor.TerminalId == null || draft.CashierShiftId == shift.Id) &&
                draft.Status == SaleDraftStatus.Draft, cancellationToken);
            if (hasDraft) return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_HAS_DRAFT");

            var now = timeProvider.GetUtcNow();
            shift.Close(context.UserId, now);
            if (accessor.TerminalId is not null)
            {
                var session = await db.Sessions.SingleAsync(item => item.Id == context.SessionId, cancellationToken);
                session.Revoke(now);
            }
            await db.SaveChangesAsync(cancellationToken);
            var response = await ToResponseAsync(db, shift, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (accessor.TerminalId is not null)
                httpContext.Response.Cookies.Delete(SessionEndpoints.CookieName, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/api"
                });
            return Results.Ok(response);
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_CLOSE_CONFLICT");
        }
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
                return true;
        return false;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static async Task<ShiftResponse> ToResponseAsync(
        PlatformAccessDbContext db, CashierShift shift, CancellationToken cancellationToken)
    {
        var totals = await db.CashLedgerMovements.AsNoTracking()
            .Where(movement => movement.CompanyId == shift.CompanyId &&
                movement.BranchId == shift.BranchId && movement.CashierId == shift.CashierId &&
                movement.CashierShiftId == shift.Id)
            .GroupBy(movement => movement.CashierShiftId)
            .Select(group => new
            {
                CashBalance = group.Where(movement => movement.Method == PaymentMethod.Cash)
                    .Sum(movement => movement.AmountDelta),
                CashSales = group.Where(movement => movement.Method == PaymentMethod.Cash &&
                    (movement.Kind == CashLedgerMovementKind.SalePayment ||
                     movement.Kind == CashLedgerMovementKind.Change))
                    .Sum(movement => movement.AmountDelta),
                CollectedSales = group.Where(movement =>
                    (movement.Kind == CashLedgerMovementKind.SalePayment ||
                     movement.Kind == CashLedgerMovementKind.Change))
                    .Sum(movement => movement.AmountDelta),
                CashCollections = group.Where(movement => movement.Method == PaymentMethod.Cash &&
                    (movement.Kind == CashLedgerMovementKind.AccountCollection ||
                     movement.Kind == CashLedgerMovementKind.AccountCollectionRefund))
                    .Sum(movement => movement.AmountDelta),
                NonCashCollections = group.Where(movement => movement.Method != PaymentMethod.Cash &&
                    (movement.Kind == CashLedgerMovementKind.AccountCollection ||
                     movement.Kind == CashLedgerMovementKind.AccountCollectionRefund))
                    .Sum(movement => movement.AmountDelta)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var cashSales = totals?.CashSales ?? 0;
        var collectedSales = totals?.CollectedSales ?? 0;
        var salesTotal = await db.ConfirmedSales.AsNoTracking()
            .Where(sale => sale.CompanyId == shift.CompanyId && sale.BranchId == shift.BranchId &&
                sale.CashierId == shift.CashierId && sale.CashierShiftId == shift.Id)
            .SumAsync(sale => (decimal?)sale.Total, cancellationToken) ?? 0;
        var accountSales = await db.CustomerSaleCharges.AsNoTracking()
            .Where(charge => charge.CompanyId == shift.CompanyId && charge.BranchId == shift.BranchId &&
                charge.CashierShiftId == shift.Id)
            .SumAsync(charge => (decimal?)charge.Amount, cancellationToken) ?? 0;
        var creditApplied = await db.CustomerCreditApplications.AsNoTracking()
            .Where(application => application.CompanyId == shift.CompanyId &&
                application.BranchId == shift.BranchId && application.CashierShiftId == shift.Id)
            .SumAsync(application => (decimal?)application.Amount, cancellationToken) ?? 0;
        return new ShiftResponse(shift.Id, shift.OpeningCash, shift.OpenedAtUtc, shift.ClosedAtUtc,
            cashSales, collectedSales - cashSales, accountSales, salesTotal, totals?.CashBalance ?? 0,
            totals?.CashCollections ?? 0, totals?.NonCashCollections ?? 0, creditApplied);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record OpenShiftRequest(decimal OpeningCash);
    private sealed record ShiftResponse(
        Guid Id, decimal OpeningCash, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc,
        decimal CashSales, decimal NonCashSales, decimal AccountSales, decimal SalesTotal, decimal CashBalance,
        decimal CashCollections, decimal NonCashCollections, decimal CreditApplied);
}
