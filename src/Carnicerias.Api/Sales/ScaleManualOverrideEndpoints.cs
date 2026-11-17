using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Sales;

public static class ScaleManualOverrideEndpoints
{
    public static IEndpointRouteBuilder MapScaleManualOverrideEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/sales/scale-overrides/authorizers", ListAuthorizersAsync)
            .RequireOperationalContext();
        endpoints.MapPost("/api/sales/scale-overrides", AuthorizeAsync)
            .RequireOperationalContext().RequireRateLimiting("withdrawal-pin");
        return endpoints;
    }

    private static async Task<IResult> ListAuthorizersAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, OperationalContextAccessService access,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var candidates = await db.Users.AsNoTracking()
            .Where(user => user.CompanyId == context.CompanyId && user.IsActive &&
                user.Id != context.UserId && user.WithdrawalPinHash != null)
            .OrderBy(user => user.UsernameNormalized)
            .Select(user => new { user.Id, user.Username })
            .ToArrayAsync(cancellationToken);
        var result = new List<object>();
        foreach (var candidate in candidates)
            if ((await access.ResolveAsync(candidate.Id, Guid.Empty,
                    context.CompanyId, context.BranchId, cancellationToken))?.Context.Permissions
                    .Contains(PlatformPermissionCatalog.PosScaleOverride) == true)
                result.Add(new { userId = candidate.Id, username = candidate.Username });
        return Results.Ok(result);
    }

    private static async Task<IResult> AuthorizeAsync(AuthorizeScaleOverrideRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        OperationalContextAccessService access, IPasswordHasher hasher,
        TimeProvider clock, HttpContext http, CancellationToken cancellationToken)
    {
        var origin = http.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, http.Request))
            return Error(403, "CSRF_REJECTED");
        if (request is null || request.ProductId == Guid.Empty ||
            request.AuthorizerUserId == Guid.Empty ||
            request.Reason is null || request.Reason.Trim().Length is < 10 or > 200 ||
            request.Pin is not { Length: 6 } pin || !pin.All(char.IsAsciiDigit))
            return Error(400, "VALIDATION_ERROR");
        var context = accessor.Context;
        if (accessor.TerminalId is not Guid terminalId)
            return Error(409, "POS_TERMINAL_REQUIRED");
        if (request.AuthorizerUserId == context.UserId)
            return Error(403, "SCALE_OVERRIDE_AUTHORIZATION_FAILED");
        var terminal = await db.PosTerminals.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == terminalId && item.CompanyId == context.CompanyId &&
            item.BranchId == context.BranchId && item.IsActive, cancellationToken);
        if (terminal?.ScalePort is null || terminal.ScaleDeviceId is Guid deviceId &&
            !await db.BranchDevices.AsNoTracking().AnyAsync(item => item.Id == deviceId &&
                item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
                item.Type == "scale" && item.IsActive, cancellationToken))
            return Error(409, "SCALE_NOT_ASSIGNED");
        var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
            item.PosTerminalId == terminalId && item.CashierId == context.UserId &&
            item.Status == CashierShiftStatus.Open, cancellationToken);
        if (shift is null) return Error(409, "CASHIER_SHIFT_REQUIRED");
        var product = await db.CatalogProducts.AsNoTracking().AnyAsync(item =>
            item.Id == request.ProductId && item.CompanyId == context.CompanyId &&
            item.IsActive && item.SaleMode == ProductSaleMode.Weight, cancellationToken);
        if (!product) return Error(400, "PRODUCT_NOT_AVAILABLE");
        var authorizer = await db.Users.AsNoTracking().SingleOrDefaultAsync(user =>
            user.Id == request.AuthorizerUserId && user.CompanyId == context.CompanyId &&
            user.IsActive && user.WithdrawalPinHash != null, cancellationToken);
        var allowed = authorizer is not null &&
            (await access.ResolveAsync(authorizer.Id, Guid.Empty,
                context.CompanyId, context.BranchId, cancellationToken))?.Context.Permissions
                .Contains(PlatformPermissionCatalog.PosScaleOverride) == true;
        if (!allowed || !hasher.Verify(pin, authorizer!.WithdrawalPinHash))
            return Error(403, "SCALE_OVERRIDE_AUTHORIZATION_FAILED");

        var now = clock.GetUtcNow();
        var grant = new ScaleManualOverride(context.CompanyId, context.BranchId,
            terminalId, shift.Id, context.UserId, request.ProductId,
            authorizer!.Id, request.Reason!, now);
        db.ScaleManualOverrides.Add(grant);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/sales/scale-overrides/{grant.Id}", new
        {
            grantId = grant.Id,
            authorizerName = authorizer.Username,
            expiresAtUtc = now.AddMinutes(15)
        });
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: status);

    private sealed record AuthorizeScaleOverrideRequest(Guid ProductId,
        Guid AuthorizerUserId, string? Pin, string? Reason);
}
