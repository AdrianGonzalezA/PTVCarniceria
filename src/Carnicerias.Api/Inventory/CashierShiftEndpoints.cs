using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
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
            item.CashierId == context.UserId && item.Status == CashierShiftStatus.Open,
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
                item.CashierId == context.UserId && item.Status == CashierShiftStatus.Closed)
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
            request.OpeningCash, timeProvider.GetUtcNow());
        db.CashierShifts.Add(shift);
        if (request.OpeningCash > 0)
            db.CashLedgerMovements.Add(new CashLedgerMovement(
                context.CompanyId, context.BranchId, shift.Id, context.UserId,
                shift.Id, PaymentMethod.Cash, CashLedgerMovementKind.Opening,
                request.OpeningCash, shift.OpenedAtUtc));
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
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var shift = await db.CashierShifts.SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
            item.CashierId == context.UserId && item.Status == CashierShiftStatus.Open,
            cancellationToken);
        if (shift is null) return Error(StatusCodes.Status409Conflict, "CASHIER_SHIFT_REQUIRED");

        shift.Close(context.UserId, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await ToResponseAsync(db, shift, cancellationToken));
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
                    movement.Kind != CashLedgerMovementKind.Opening).Sum(movement => movement.AmountDelta),
                SalesTotal = group.Where(movement => movement.Kind != CashLedgerMovementKind.Opening)
                    .Sum(movement => movement.AmountDelta)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var cashSales = totals?.CashSales ?? 0;
        var salesTotal = totals?.SalesTotal ?? 0;
        return new ShiftResponse(shift.Id, shift.OpeningCash, shift.OpenedAtUtc, shift.ClosedAtUtc,
            cashSales, salesTotal - cashSales, salesTotal, totals?.CashBalance ?? 0);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record OpenShiftRequest(decimal OpeningCash);
    private sealed record ShiftResponse(
        Guid Id, decimal OpeningCash, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc,
        decimal CashSales, decimal NonCashSales, decimal SalesTotal, decimal CashBalance);
}
