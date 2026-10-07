using Carnicerias.Api.Contracts;
using Carnicerias.Api.Sessions;
using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Security;

public sealed class OperationalAuthorizationFilter(string? requiredPermission) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        var sessions = services.GetRequiredService<SessionAuthenticationService>();
        var contexts = services.GetRequiredService<OperationalContextAccessService>();
        var accessor = services.GetRequiredService<OperationalContextAccessor>();
        var current = await sessions.FindActiveAsync(
            httpContext.Request.Cookies[SessionEndpoints.CookieName],
            httpContext.RequestAborted);

        if (current is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "NOT_AUTHENTICATED");
        }

        var authorized = await contexts.ResolveSessionAsync(current.Session, httpContext.RequestAborted);
        if (authorized is null)
        {
            return Error(StatusCodes.Status403Forbidden, "OPERATIONAL_CONTEXT_REQUIRED");
        }

        if (requiredPermission is not null && !authorized.Context.Permissions.Contains(requiredPermission))
        {
            return Error(StatusCodes.Status403Forbidden, "PERMISSION_REQUIRED");
        }

        accessor.Set(authorized.Context);
        return await next(context);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);
}
