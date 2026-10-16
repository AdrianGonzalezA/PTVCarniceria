using Carnicerias.Api.Contracts;
using Carnicerias.Api.Organization;
using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Security;

public sealed class ManufacturerAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionAuthenticationService>();
        var current = await sessions.FindManufacturerActiveAsync(
            context.HttpContext.Request.Cookies[ManufacturerEndpoints.CookieName],
            context.HttpContext.RequestAborted);
        if (current is null)
            return Results.Json(new ErrorResponse(new ApiError("NOT_AUTHENTICATED",
                "No se pudo completar la solicitud", [])), statusCode: StatusCodes.Status401Unauthorized);

        return await next(context);
    }
}
