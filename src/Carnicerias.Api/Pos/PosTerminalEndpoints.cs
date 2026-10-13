using Carnicerias.Api.Contracts;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

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
        PlatformAccessDbContext db,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        var terminal = await terminals.FindActiveAsync(
            httpContext.Request.Headers[CredentialHeader].ToString(), cancellationToken);
        if (terminal is null)
            return Results.Json(new ErrorResponse(new ApiError(
                "POS_TERMINAL_REQUIRED", "Caja no habilitada", [])),
                statusCode: StatusCodes.Status401Unauthorized);

        var identity = await db.Companies.AsNoTracking()
            .Where(company => company.Id == terminal.CompanyId && company.IsActive)
            .Join(db.Branches.AsNoTracking().Where(branch =>
                    branch.Id == terminal.BranchId && branch.IsActive),
                company => company.Id, branch => branch.CompanyId,
                (company, branch) => new { CompanyName = company.Name, BranchName = branch.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null)
            return Results.Json(new ErrorResponse(new ApiError(
                "POS_TERMINAL_REQUIRED", "Caja no habilitada", [])),
                statusCode: StatusCodes.Status401Unauthorized);

        return Results.Ok(new TerminalResponse(
            terminal.Id, terminal.Name, terminal.CompanyId, identity.CompanyName,
            terminal.BranchId, identity.BranchName));
    }

    private sealed record TerminalResponse(Guid Id, string Name, Guid CompanyId,
        string CompanyName, Guid BranchId, string BranchName);
}
