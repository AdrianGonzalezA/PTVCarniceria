using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Sessions;

public static class SessionEndpoints
{
    public const string CookieName = "carnicerias.session";
    private const string CookiePath = "/api";

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sessions", CreateSessionAsync);
        endpoints.MapGet("/api/sessions/current", GetCurrentSessionAsync);
        endpoints.MapDelete("/api/sessions/current", DeleteCurrentSessionAsync);
        endpoints.MapGet("/api/operational-contexts", GetOperationalContextsAsync);
        endpoints.MapPut("/api/sessions/current/context", SelectOperationalContextAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateSessionAsync(
        LoginRequest? request,
        SessionAuthenticationService sessions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Credential) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            request.Credential.Length > 320 ||
            System.Text.Encoding.UTF8.GetByteCount(request.Password) > 1024)
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }

        var created = await sessions.CreateAsync(request.Credential, request.Password, cancellationToken);
        if (created is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_FAILED");
        }

        httpContext.Response.Cookies.Append(CookieName, created.Credential, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = created.Session.ExpiresAtUtc
        });

        return Results.Ok(new SessionResponse(
            created.User.Id,
            created.User.Username,
            created.Session.ExpiresAtUtc,
            null));
    }

    private static async Task<IResult> GetCurrentSessionAsync(
        SessionAuthenticationService sessions,
        OperationalContextAccessService contexts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var credential = httpContext.Request.Cookies[CookieName];
        var current = await sessions.FindActiveAsync(credential, cancellationToken);
        if (current is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "NOT_AUTHENTICATED");
        }

        var context = await contexts.ResolveSessionAsync(current.Session, cancellationToken);
        return Results.Ok(new SessionResponse(
            current.User.Id,
            current.User.Username,
            current.Session.ExpiresAtUtc,
            ToResponse(context)));
    }

    private static async Task<IResult> GetOperationalContextsAsync(
        SessionAuthenticationService sessions,
        OperationalContextAccessService contexts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var current = await sessions.FindActiveAsync(
            httpContext.Request.Cookies[CookieName],
            cancellationToken);
        if (current is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "NOT_AUTHENTICATED");
        }

        var options = await contexts.ListAsync(current.User.Id, cancellationToken);
        return Results.Ok(options.Select(company => new OperationalCompanyResponse(
            company.CompanyId,
            company.CompanyName,
            company.Branches.Select(branch => new OperationalBranchResponse(branch.BranchId, branch.BranchName))
                .ToArray())));
    }

    private static async Task<IResult> SelectOperationalContextAsync(
        SelectOperationalContextRequest? request,
        SessionAuthenticationService sessions,
        OperationalContextAccessService contexts,
        OperationalContextChangeGuard contextChangeGuard,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
        {
            return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        }

        if (request is null || request.CompanyId == Guid.Empty || request.BranchId == Guid.Empty)
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }

        var current = await sessions.FindActiveAsync(
            httpContext.Request.Cookies[CookieName],
            cancellationToken);
        if (current is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "NOT_AUTHENTICATED");
        }

        var requestedContext = await contexts.ResolveAsync(
            current.User.Id,
            current.Session.Id,
            request.CompanyId,
            request.BranchId,
            cancellationToken);
        if (requestedContext is null)
        {
            return Error(StatusCodes.Status403Forbidden, "CONTEXT_NOT_ASSIGNED");
        }

        var currentContext = await contexts.ResolveSessionAsync(current.Session, cancellationToken);
        if (currentContext is not null &&
            (currentContext.Context.CompanyId != request.CompanyId ||
             currentContext.Context.BranchId != request.BranchId))
        {
            var check = await contextChangeGuard.CheckAsync(currentContext.Context, cancellationToken);
            if (!check.IsAvailable)
            {
                return Error(StatusCodes.Status503ServiceUnavailable, "CONTEXT_BLOCK_CHECK_UNAVAILABLE");
            }

            if (check.Blocks.Count > 0)
            {
                return Error(
                    StatusCodes.Status409Conflict,
                    "CONTEXT_CHANGE_BLOCKED",
                    check.Blocks.Cast<object>().ToArray());
            }
        }

        var context = await contexts.SelectAsync(
            current.Session,
            request.CompanyId,
            request.BranchId,
            cancellationToken);
        if (context is null)
        {
            return Error(StatusCodes.Status403Forbidden, "CONTEXT_NOT_ASSIGNED");
        }

        return Results.Ok(new SessionResponse(
            current.User.Id,
            current.User.Username,
            current.Session.ExpiresAtUtc,
            ToResponse(context)));
    }

    private static async Task<IResult> DeleteCurrentSessionAsync(
        SessionAuthenticationService sessions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
        {
            return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        }

        await sessions.RevokeAsync(httpContext.Request.Cookies[CookieName], cancellationToken);
        httpContext.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath
        });
        return Results.NoContent();
    }

    private static OperationalContextResponse? ToResponse(AuthorizedOperationalContext? context) => context is null
        ? null
        : new OperationalContextResponse(
            context.Context.UserId,
            context.Context.CompanyId,
            context.CompanyName,
            context.Context.BranchId,
            context.BranchName,
            context.Context.Permissions.Order(StringComparer.Ordinal).ToArray(),
            context.Context.SessionId);

    private static IResult Error(int statusCode, string code, IReadOnlyList<object>? details = null) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", details ?? [])),
        statusCode: statusCode);

    private sealed record LoginRequest(string? Credential, string? Password);

    private sealed record SelectOperationalContextRequest(Guid CompanyId, Guid BranchId);
}
