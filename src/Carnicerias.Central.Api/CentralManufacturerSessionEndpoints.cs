using System.Text;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Api;

public static class CentralManufacturerSessionEndpoints
{
    public const string CookieName = "carnicerias.central.manufacturer";
    private const string CookiePath = "/api/v1/manufacturer";
    private const int MaximumRequestBytes = 4096;

    public static void MapCentralManufacturerSessionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/manufacturer/sessions", LoginAsync)
            .RequireRateLimiting("central-login");
        routes.MapGet("/api/v1/manufacturer/sessions/current", CurrentAsync);
        routes.MapDelete("/api/v1/manufacturer/sessions/current", LogoutAsync);
    }

    private static async Task<IResult> LoginAsync(HttpContext context,
        CentralManufacturerSessionService service, CentralLoginLimiter accountLimiter,
        CancellationToken cancellationToken)
    {
        if (!CentralRequestSecurity.HasSameOriginAndCsrfHeader(context))
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");

        var request = await CentralJsonRequest.ReadAsync<LoginRequest>(context.Request,
            MaximumRequestBytes, cancellationToken);
        if (request is null || !Valid(request))
            return Error(400, "INVALID_LOGIN_REQUEST", "Datos de acceso inválidos.");
        if (!accountLimiter.TryAcquire("CENTRAL_MANUFACTURER", request.Credential!))
            return Error(429, "LOGIN_RATE_LIMITED", "Demasiados intentos. Reintentá más tarde.");

        var issued = await service.LoginAsync(request.Credential, request.Password,
            cancellationToken);
        if (issued is null)
            return Error(401, "INVALID_CREDENTIALS", "Credenciales inválidas.");

        context.Response.Cookies.Append(CookieName, issued.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = issued.Session.ExpiresAtUtc,
            IsEssential = true
        });
        return Results.Ok(ToResponse(issued.Session, issued.Manufacturer));
    }

    private static async Task<IResult> CurrentAsync(HttpContext context,
        CentralManufacturerSessionService service, CancellationToken cancellationToken)
    {
        var current = await service.AuthenticateAsync(context.Request.Cookies[CookieName],
            cancellationToken);
        return current is null
            ? Error(401, "SESSION_REQUIRED", "Sesión de SuperAdmin central requerida.")
            : Results.Ok(ToResponse(current.Session, current.Manufacturer));
    }

    private static async Task<IResult> LogoutAsync(HttpContext context,
        CentralManufacturerSessionService service, CancellationToken cancellationToken)
    {
        if (!CentralRequestSecurity.HasSameOriginAndCsrfHeader(context))
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        if (!await service.LogoutAsync(context.Request.Cookies[CookieName], cancellationToken))
            return Error(401, "SESSION_REQUIRED", "Sesión de SuperAdmin central requerida.");

        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = CookiePath
        });
        return Results.NoContent();
    }

    private static object ToResponse(CentralManufacturerSession session,
        CentralManufacturer manufacturer) => new
        {
            userId = manufacturer.Id,
            username = manufacturer.Username,
            expiresAtUtc = session.ExpiresAtUtc
        };

    private static bool Valid(LoginRequest request) =>
        request.Credential is { Length: >= 1 and <= 100 }
        && !request.Credential.Contains('@')
        && !request.Credential.Any(char.IsControl)
        && request.Password is { Length: >= 1 }
        && Encoding.UTF8.GetByteCount(request.Password) <= 1024;

    private static IResult Error(int status, string code, string message) => Results.Json(
        new { error = new { code, message, details = (object?)null } }, statusCode: status);

    private sealed record LoginRequest(string? Credential, string? Password);
}
