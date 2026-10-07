using Carnicerias.Api.Contracts;
using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Pos;

public static class PosTerminalEndpoints
{
    public const string CredentialHeader = "X-Pos-Terminal-Credential";

    public static IEndpointRouteBuilder MapPosTerminalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/pos-terminals/current", GetCurrentAsync);
        return endpoints;
    }

    private static async Task<IResult> GetCurrentAsync(
        HttpContext httpContext,
        PosTerminalAuthenticationService terminals,
        CancellationToken cancellationToken)
    {
        var terminal = await terminals.FindActiveAsync(
            httpContext.Request.Headers[CredentialHeader].ToString(), cancellationToken);
        if (terminal is null)
            return Results.Json(new ErrorResponse(new ApiError(
                "POS_TERMINAL_REQUIRED", "Caja no habilitada", [])),
                statusCode: StatusCodes.Status401Unauthorized);

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new TerminalResponse(
            terminal.Id, terminal.Name, terminal.CompanyId, terminal.BranchId));
    }

    private sealed record TerminalResponse(Guid Id, string Name, Guid CompanyId, Guid BranchId);
}
