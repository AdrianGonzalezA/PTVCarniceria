using System.Security.Cryptography;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Payments;

public static class CloverOAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminCloverOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/payment-gateways/clover")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        group.MapGet("", GetAsync);
        group.MapPut("", SaveAsync);
        group.MapPost("/authorization", AuthorizeAsync);
        group.MapPost("/connection", ProbeConnectionAsync);
        group.MapPost("/devices", ListDevicesAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapPublicCloverOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/clover", () => Results.Ok(new { status = "ready", oauth = "v2" }));
        endpoints.MapGet("/clover/status", () => Results.Ok(new { status = "ready", oauth = "v2" }));
        endpoints.MapGet("/clover/launch", LaunchAsync).RequireRateLimiting("clover-public");
        endpoints.MapGet("/clover/callback", CallbackAsync).RequireRateLimiting("clover-public");
        return endpoints;
    }

    private static async Task<IResult> GetAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, TimeProvider clock, CancellationToken cancellationToken)
    {
        var settings = await db.CloverGatewaySettings.AsNoTracking().SingleOrDefaultAsync(
            item => item.CompanyId == accessor.Context.CompanyId, cancellationToken);
        return Results.Ok(new { provider = "clover", environment = "sandbox",
            clientId = settings?.ClientId ?? "", merchantId = settings?.MerchantId ?? "",
            hasClientSecret = settings?.HasClientSecret ?? false,
            isConnected = settings?.IsConnected == true &&
                settings.RefreshTokenExpiresAtUtc > clock.GetUtcNow() &&
                settings.RefreshAttemptId is null,
            requiresReauthorization = settings?.RefreshAttemptId is not null ||
                settings?.IsConnected == true && settings.RefreshTokenExpiresAtUtc <= clock.GetUtcNow(),
            accessTokenExpiresAtUtc = settings?.AccessTokenExpiresAtUtc,
            refreshTokenExpiresAtUtc = settings?.RefreshTokenExpiresAtUtc });
    }

    private static async Task<IResult> SaveAsync(CloverSettingsRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CloverSettingsProtector protector, TimeProvider clock, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.ClientSecret?.Length > 4096 ||
            request.ClientSecret is { Length: > 0 } secret && secret.Any(char.IsWhiteSpace))
            return Error(400, "VALIDATION_ERROR");

        var companyId = accessor.Context.CompanyId;
        var actorId = accessor.Context.UserId;
        var now = clock.GetUtcNow();
        var settings = await db.CloverGatewaySettings.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId, cancellationToken);
        try
        {
            if (settings is null)
            {
                settings = new CloverGatewaySettings(companyId, request.ClientId,
                    request.MerchantId, actorId, now);
                db.CloverGatewaySettings.Add(settings);
            }
            else settings.UpdateIdentity(request.ClientId, request.MerchantId, actorId, now);
            if (!string.IsNullOrEmpty(request.ClientSecret))
                settings.ReplaceClientSecret(protector.Protect(request.ClientSecret), actorId, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException) { return Error(400, "VALIDATION_ERROR"); }
        catch (DbUpdateConcurrencyException) { return Error(409, "CONFIGURATION_CHANGED"); }
        catch (DbUpdateException) { return Error(409, "MERCHANT_CONFLICT"); }

        return Results.Ok(new { settings.ClientId, settings.MerchantId,
            settings.HasClientSecret, settings.IsConnected });
    }

    private static async Task<IResult> AuthorizeAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, TimeProvider clock, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return Error(403, "CSRF_REJECTED");
        var settings = await db.CloverGatewaySettings.AsNoTracking().SingleOrDefaultAsync(
            item => item.CompanyId == accessor.Context.CompanyId, cancellationToken);
        if (settings is not { HasClientSecret: true }) return Error(409, "CLOVER_NOT_CONFIGURED");

        var now = clock.GetUtcNow();
        var state = CloverOAuthFlow.NewState();
        db.CloverOAuthAttempts.Add(new CloverOAuthAttempt(CloverOAuthFlow.HashState(state),
            settings.CompanyId, accessor.Context.UserId, now, now.AddMinutes(10)));
        await db.SaveChangesAsync(cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { authorizeUrl = CloverOAuthFlow.AuthorizeUrl(settings.ClientId, state),
            expiresAtUtc = now.AddMinutes(10) });
    }

    private static async Task<IResult> ProbeConnectionAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CloverAccessTokenResolver resolver,
        CloverMerchantProbeClient client, HttpContext context, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return ProbeError(403, "CSRF_REJECTED");
        var access = await resolver.ResolveAsync(accessor.Context.CompanyId,
            accessor.Context.UserId, cancellationToken);
        if (access.ErrorCode is { } code)
            return ProbeError(code is "CLOVER_REFRESH_UNCERTAIN" or "CLOVER_CREDENTIAL_UNAVAILABLE"
                ? 503 : 409, code);

        CloverMerchantProbeOutcome outcome;
        try
        {
            outcome = await client.ProbeAsync(access.MerchantId!, access.Token!, cancellationToken);
        }
        catch (HttpRequestException) { return ProbeError(502, "CLOVER_UNAVAILABLE"); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ProbeError(502, "CLOVER_UNAVAILABLE"); }

        var stillCurrent = await db.CloverGatewaySettings.AsNoTracking().AnyAsync(item =>
            item.CompanyId == accessor.Context.CompanyId &&
            item.UpdatedAtUtc == access.UpdatedAtUtc, cancellationToken);
        if (!stillCurrent) return ProbeError(409, "CLOVER_CONFIGURATION_CHANGED");
        return outcome switch
        {
            CloverMerchantProbeOutcome.Verified => Results.Ok(new { status = "verified", environment = "sandbox" }),
            CloverMerchantProbeOutcome.Unauthorized => ProbeError(409, "CLOVER_TOKEN_REJECTED"),
            CloverMerchantProbeOutcome.MerchantMismatch => ProbeError(409, "CLOVER_MERCHANT_MISMATCH"),
            _ => ProbeError(502, "CLOVER_UNAVAILABLE"),
        };
    }

    private static async Task<IResult> ListDevicesAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CloverAccessTokenResolver resolver,
        CloverDeviceInventoryClient client, HttpContext context, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return ProbeError(403, "CSRF_REJECTED");
        var access = await resolver.ResolveAsync(accessor.Context.CompanyId,
            accessor.Context.UserId, cancellationToken);
        if (access.ErrorCode is { } code)
            return ProbeError(code is "CLOVER_REFRESH_UNCERTAIN" or "CLOVER_CREDENTIAL_UNAVAILABLE"
                ? 503 : 409, code);

        CloverDeviceInventoryResult inventory;
        try { inventory = await client.ListAsync(access.MerchantId!, access.Token!, cancellationToken); }
        catch (HttpRequestException) { return ProbeError(502, "CLOVER_UNAVAILABLE"); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ProbeError(502, "CLOVER_UNAVAILABLE"); }

        var stillCurrent = await db.CloverGatewaySettings.AsNoTracking().AnyAsync(item =>
            item.CompanyId == accessor.Context.CompanyId &&
            item.UpdatedAtUtc == access.UpdatedAtUtc, cancellationToken);
        if (!stillCurrent) return ProbeError(409, "CLOVER_CONFIGURATION_CHANGED");
        if (inventory.ErrorCode is { } error)
            return ProbeError(error == "CLOVER_TOKEN_REJECTED" ? 409 : 502, error);
        return Results.Ok(new { environment = "sandbox", devices = inventory.Devices });
    }

    private static async Task<IResult> LaunchAsync(HttpContext context,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var merchantId = QueryValue(context, "merchant_id") ?? QueryValue(context, "merchantId");
        if (!CloverOAuthFlow.ValidMerchantId(merchantId)) return PublicError(400);
        var db = context.RequestServices.GetRequiredService<PlatformAccessDbContext>();
        var settings = await db.CloverGatewaySettings.AsNoTracking().SingleOrDefaultAsync(
            item => item.MerchantId == merchantId, cancellationToken);
        if (settings is not { HasClientSecret: true }) return PublicError(404);
        if (!await HasManagingUserAsync(db, settings.UpdatedByUserId,
            settings.CompanyId, cancellationToken)) return PublicError(403);

        var now = clock.GetUtcNow();
        var state = CloverOAuthFlow.NewState();
        db.CloverOAuthAttempts.Add(new CloverOAuthAttempt(CloverOAuthFlow.HashState(state),
            settings.CompanyId, settings.UpdatedByUserId, now, now.AddMinutes(10)));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Redirect(CloverOAuthFlow.AuthorizeUrl(settings.ClientId, state));
    }

    private static async Task<IResult> CallbackAsync(HttpContext context,
        CloverSettingsProtector protector,
        CloverOAuthClient client, TimeProvider clock, CancellationToken cancellationToken)
    {
        var state = QueryValue(context, "state");
        var code = QueryValue(context, "code");
        var merchantId = QueryValue(context, "merchant_id");
        if (!CloverOAuthFlow.ValidCallback(state, code, merchantId)) return PublicError(400);

        var db = context.RequestServices.GetRequiredService<PlatformAccessDbContext>();
        var now = clock.GetUtcNow();
        var stateHash = CloverOAuthFlow.HashState(state!);
        var attempt = await db.CloverOAuthAttempts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.StateHash == stateHash, cancellationToken);
        if (attempt is null || attempt.ExpiresAtUtc <= now || attempt.ConsumedAtUtc is not null)
            return PublicError(400);
        var settings = await db.CloverGatewaySettings.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == attempt.CompanyId, cancellationToken);
        if (settings is not { HasClientSecret: true } || settings.MerchantId != merchantId)
            return PublicError(400);
        if (!await HasManagingUserAsync(db, attempt.UserId,
            attempt.CompanyId, cancellationToken)) return PublicError(403);

        var consumed = await db.CloverOAuthAttempts.Where(item => item.StateHash == stateHash &&
            item.ConsumedAtUtc == null && item.ExpiresAtUtc > now).ExecuteUpdateAsync(
            updates => updates.SetProperty(item => item.ConsumedAtUtc, now), cancellationToken);
        if (consumed != 1) return PublicError(400);

        CloverOAuthTokenResponse? tokens;
        try
        {
            var secret = protector.Unprotect(settings.ProtectedClientSecret!);
            tokens = await client.ExchangeAsync(settings.ClientId, secret, code!, now, cancellationToken);
        }
        catch (CryptographicException) { return PublicError(503); }
        catch (HttpRequestException) { return PublicError(502); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return PublicError(502); }
        if (tokens is null) return PublicError(502);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var currentRows = await db.CloverGatewaySettings.FromSqlInterpolated(
            $"SELECT * FROM payments_cash.clover_gateway_settings WHERE \"CompanyId\" = {attempt.CompanyId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var current = currentRows.SingleOrDefault();
        if (current is null || current.ClientId != settings.ClientId ||
            current.MerchantId != settings.MerchantId ||
            current.ProtectedClientSecret is null ||
            !current.ProtectedClientSecret.AsSpan().SequenceEqual(settings.ProtectedClientSecret!))
            return PublicError(409);
        try
        {
            current.ReplaceTokens(protector.Protect(tokens.AccessToken),
                protector.Protect(tokens.RefreshToken), tokens.AccessExpiresAtUtc,
                tokens.RefreshExpiresAtUtc, attempt.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return PublicError(409); }
        return Results.Content("<!doctype html><html lang=\"es\"><meta charset=\"utf-8\">" +
            "<title>Clover conectado</title><body><h1>Clover conectado</h1>" +
            "<p>La cuenta sandbox quedó vinculada. Podés volver a la administración.</p></body></html>",
            "text/html; charset=utf-8");
    }

    private static string? QueryValue(HttpContext context, string key) =>
        context.Request.Query.TryGetValue(key, out var value) && value.Count == 1 ? value[0] : null;

    private static Task<bool> HasManagingUserAsync(PlatformAccessDbContext db,
        Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        (from user in db.Users.AsNoTracking()
         join assignment in db.UserAssignments.AsNoTracking() on user.Id equals assignment.UserId
         join grant in db.RolePermissions.AsNoTracking() on assignment.RoleId equals grant.RoleId
         join permission in db.Permissions.AsNoTracking() on grant.PermissionId equals permission.Id
         where user.Id == userId && user.IsActive && assignment.CompanyId == companyId &&
               permission.Code == PlatformPermissionCatalog.OrganizationManage
         select user.Id).AnyAsync(cancellationToken);

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int status, string code) => Results.Json(new
    {
        error = new { code, message = "No se pudo configurar Clover" }
    }, statusCode: status);

    private static IResult ProbeError(int status, string code) => Results.Json(new
    {
        error = new { code, message = "No se pudo verificar Clover" }
    }, statusCode: status);

    private static IResult PublicError(int status) => Results.Text(
        "No se pudo completar la autorización de Clover. Iniciá una nueva autorización desde administración.",
        "text/plain; charset=utf-8", statusCode: status);

    private sealed record CloverSettingsRequest(string ClientId, string MerchantId, string? ClientSecret);
}
