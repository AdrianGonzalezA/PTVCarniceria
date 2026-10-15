using System.Text;
using System.Text.Json;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Api;

public static class CentralSessionEndpoints
{
    public const string CookieName = "carnicerias.central.session";
    private const int MaximumRequestBytes = 4096;

    public static void MapCentralSessionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/sessions", LoginAsync).RequireRateLimiting("central-login");
        routes.MapGet("/api/v1/sessions/current", CurrentAsync);
        routes.MapDelete("/api/v1/sessions/current", LogoutAsync);
    }

    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        CentralSessionService service,
        CentralLoginLimiter accountLimiter,
        CancellationToken cancellationToken)
    {
        if (!HasSameOriginAndCsrfHeader(httpContext))
        {
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        }

        var request = await ReadLoginAsync(httpContext.Request, cancellationToken);
        if (request is null || !IsValid(request))
        {
            return Error(400, "INVALID_LOGIN_REQUEST", "Datos de acceso inválidos.");
        }

        if (!accountLimiter.TryAcquire(request.CompanyCode!, request.Credential!))
        {
            return Error(429, "LOGIN_RATE_LIMITED", "Demasiados intentos. Reintentá más tarde.");
        }

        var issued = await service.LoginAsync(request.CompanyCode, request.Credential,
            request.Password, cancellationToken);
        if (issued is null)
        {
            return Error(401, "INVALID_CREDENTIALS", "Credenciales inválidas.");
        }

        // CookieOptions: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.cookieoptions?view=aspnetcore-10.0
        httpContext.Response.Cookies.Append(CookieName, issued.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1",
            Expires = issued.Session.ExpiresAtUtc,
            IsEssential = true
        });
        return Results.Ok(ToResponse(issued.Session, issued.Company, issued.Administrator));
    }

    private static async Task<IResult> CurrentAsync(
        HttpContext httpContext,
        CentralSessionService service,
        CancellationToken cancellationToken)
    {
        var session = await service.AuthenticateAsync(
            httpContext.Request.Cookies[CookieName], cancellationToken);
        return session is null
            ? Error(401, "SESSION_REQUIRED", "Sesión central requerida.")
            : Results.Ok(ToResponse(session.Session, session.Company, session.Administrator));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        CentralSessionService service,
        CancellationToken cancellationToken)
    {
        if (!HasSameOriginAndCsrfHeader(httpContext))
        {
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        }

        if (!await service.LogoutAsync(httpContext.Request.Cookies[CookieName], cancellationToken))
        {
            return Error(401, "SESSION_REQUIRED", "Sesión central requerida.");
        }

        httpContext.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1"
        });
        return Results.NoContent();
    }

    private static object ToResponse(CentralSession session,
        CentralCompany company, CentralAdministrator administrator) => new
    {
        userId = administrator.Id,
        centralCompanyId = company.Id,
        username = administrator.Username,
        expiresAtUtc = session.ExpiresAtUtc
    };

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new { error = new { code, message, details = (object?)null } },
            statusCode: statusCode);

    private static bool HasSameOriginAndCsrfHeader(HttpContext context)
    {
        if (context.Request.Headers["X-Carnicerias-CSRF"] != "1"
            || context.Request.Headers.Origin.Count != 1
            || !Uri.TryCreate(context.Request.Headers.Origin[0], UriKind.Absolute, out var origin))
        {
            return false;
        }

        var request = context.Request;
        var requestPort = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return origin.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase)
            && origin.Host.Equals(request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == requestPort
            && origin.UserInfo.Length == 0
            && origin.AbsolutePath == "/"
            && origin.Query.Length == 0
            && origin.Fragment.Length == 0;
    }

    private static bool IsValid(CentralLoginRequest request) =>
        request.CompanyCode is { Length: >= 2 and <= 40 }
        && request.CompanyCode.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
        && request.Credential is { Length: >= 1 and <= 100 }
        && !request.Credential.Any(char.IsControl)
        && request.Password is { Length: >= 1 }
        && Encoding.UTF8.GetByteCount(request.Password) <= 1024;

    private static async Task<CentralLoginRequest?> ReadLoginAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType() || request.ContentLength > MaximumRequestBytes)
        {
            return null;
        }

        var bytes = new byte[MaximumRequestBytes + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await request.Body.ReadAsync(bytes.AsMemory(count), cancellationToken);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count is 0 or > MaximumRequestBytes)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CentralLoginRequest>(
                bytes.AsSpan(0, count), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CentralLoginRequest(
        string? CompanyCode, string? Credential, string? Password);
}
