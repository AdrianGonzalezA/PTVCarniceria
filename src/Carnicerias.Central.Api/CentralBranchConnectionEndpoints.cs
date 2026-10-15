using System.Globalization;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Api;

public static class CentralBranchConnectionEndpoints
{
    private const int MaximumRequestBytes = 4096;

    public static void MapCentralBranchConnectionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/branch-connections", ListAsync)
            .RequireRateLimiting("central-connections");
        routes.MapPost("/api/v1/branch-connections", CreateAsync)
            .RequireRateLimiting("central-connections");
        routes.MapPost("/api/v1/branch-connections/{installationId:guid}/checks", CheckAsync)
            .RequireRateLimiting("central-connections");
        routes.MapPatch("/api/v1/branch-connections/{installationId:guid}", EditAsync)
            .RequireRateLimiting("central-connections");
        routes.MapPatch("/api/v1/branch-connections/{installationId:guid}/state", ChangeStateAsync)
            .RequireRateLimiting("central-connections");
    }

    private static async Task<IResult> CheckAsync(Guid installationId, HttpContext context,
        CentralSessionService sessions, CentralBranchConnectionService service,
        CancellationToken cancellationToken)
    {
        if (!CentralRequestSecurity.HasSameOriginAndCsrfHeader(context))
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        var session = await sessions.AuthenticateAsync(
            context.Request.Cookies[CentralSessionEndpoints.CookieName], cancellationToken);
        if (session is null) return Error(401, "SESSION_REQUIRED", "Sesion central requerida.");
        try
        {
            var result = await service.CheckAsync(session.Company.Id, session.Administrator.Id,
                installationId, cancellationToken);
            return Results.Ok(result);
        }
        catch (CentralBranchConnectionNotFoundException)
        { return Error(404, "BRANCH_CONNECTION_NOT_FOUND", "Conexion no encontrada."); }
        catch (CentralBranchConnectionConflictException)
        { return Error(409, "BRANCH_CONNECTION_CONFLICT", "La conexion cambio; recarga el listado."); }
    }

    private static async Task<IResult> EditAsync(Guid installationId, HttpContext context,
        CentralSessionService sessions, CentralBranchConnectionService service,
        CancellationToken cancellationToken)
    {
        if (!CentralRequestSecurity.HasSameOriginAndCsrfHeader(context))
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        var session = await sessions.AuthenticateAsync(
            context.Request.Cookies[CentralSessionEndpoints.CookieName], cancellationToken);
        if (session is null) return Error(401, "SESSION_REQUIRED", "Sesion central requerida.");
        var request = await CentralJsonRequest.ReadAsync<EditRequest>(context.Request,
            MaximumRequestBytes, cancellationToken, rejectUnknownFields: true);
        if (request is null)
            return Error(400, "INVALID_BRANCH_CONNECTION", "Datos de conexion invalidos.");
        try
        {
            var result = await service.EditAsync(session.Company.Id, session.Administrator.Id,
                installationId, new CentralBranchConnectionEdit(request.Label,
                    request.DestinationUrl, request.Token), cancellationToken);
            return Results.Ok(result);
        }
        catch (ArgumentException)
        { return Error(400, "INVALID_BRANCH_CONNECTION", "Datos de conexion invalidos."); }
        catch (CentralBranchConnectionNotFoundException)
        { return Error(404, "BRANCH_CONNECTION_NOT_FOUND", "Conexion no encontrada."); }
        catch (CentralBranchConnectionConflictException)
        { return Error(409, "BRANCH_CONNECTION_CONFLICT", "La conexion cambio; recarga el listado."); }
        catch (CentralBranchConnectionVerificationException)
        { return Error(422, "BRANCH_CONNECTION_UNVERIFIED", "No se pudo verificar la sucursal."); }
        catch (InvalidOperationException)
        { return Error(503, "BRANCH_CREDENTIAL_UNAVAILABLE", "Credenciales centrales no disponibles."); }
    }

    private static async Task<IResult> ChangeStateAsync(Guid installationId, HttpContext context,
        CentralSessionService sessions, CentralBranchConnectionService service,
        CancellationToken cancellationToken)
    {
        if (!CentralRequestSecurity.HasSameOriginAndCsrfHeader(context))
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        var session = await sessions.AuthenticateAsync(
            context.Request.Cookies[CentralSessionEndpoints.CookieName], cancellationToken);
        if (session is null) return Error(401, "SESSION_REQUIRED", "Sesion central requerida.");
        var request = await CentralJsonRequest.ReadAsync<StateRequest>(context.Request,
            MaximumRequestBytes, cancellationToken, rejectUnknownFields: true);
        if (request?.IsActive is null)
            return Error(400, "INVALID_BRANCH_CONNECTION_STATE", "Estado invalido.");
        try
        {
            var result = await service.ChangeStateAsync(session.Company.Id,
                session.Administrator.Id, installationId, request.IsActive.Value,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (CentralBranchConnectionNotFoundException)
        { return Error(404, "BRANCH_CONNECTION_NOT_FOUND", "Conexion no encontrada."); }
        catch (CentralBranchConnectionConflictException)
        { return Error(409, "BRANCH_CONNECTION_CONFLICT", "La conexion cambio; recarga el listado."); }
        catch (CentralBranchConnectionVerificationException)
        { return Error(422, "BRANCH_CONNECTION_UNVERIFIED", "No se pudo verificar la sucursal."); }
    }

    private static async Task<IResult> ListAsync(HttpContext context,
        CentralSessionService sessions, CentralBranchConnectionService service,
        CancellationToken cancellationToken)
    {
        var session = await sessions.AuthenticateAsync(
            context.Request.Cookies[CentralSessionEndpoints.CookieName], cancellationToken);
        if (session is null) return Error(401, "SESSION_REQUIRED", "Sesión central requerida.");
        if (!TryReadPage(context.Request.Query, out var page, out var pageSize))
            return Error(400, "INVALID_PAGINATION", "Paginación inválida.");

        var result = await service.ListAsync(session.Company.Id, page, pageSize,
            cancellationToken);
        return Results.Ok(new
        {
            data = result.Data,
            pagination = new
            {
                page = result.Page, pageSize = result.PageSize,
                totalCount = result.TotalCount,
                totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize)
            }
        });
    }

    private static async Task<IResult> CreateAsync(HttpContext context,
        CentralSessionService sessions, CentralBranchConnectionService service,
        CancellationToken cancellationToken)
    {
        if (!CentralRequestSecurity.HasSameOriginAndCsrfHeader(context))
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        var session = await sessions.AuthenticateAsync(
            context.Request.Cookies[CentralSessionEndpoints.CookieName], cancellationToken);
        if (session is null) return Error(401, "SESSION_REQUIRED", "Sesión central requerida.");

        var request = await CentralJsonRequest.ReadAsync<CreateRequest>(context.Request,
            MaximumRequestBytes, cancellationToken, rejectUnknownFields: true);
        if (request is null || request.InstallationId == Guid.Empty
            || request.RemoteCompanyId == Guid.Empty || request.RemoteBranchId == Guid.Empty
            || request.Label is null || request.DestinationUrl is null
            || request.Token is null)
            return Error(400, "INVALID_BRANCH_CONNECTION", "Datos de conexión inválidos.");

        try
        {
            var result = await service.RegisterAsync(session.Company.Id,
                session.Administrator.Id, new CentralBranchConnectionCreate(
                    request.InstallationId, request.RemoteCompanyId,
                    request.RemoteBranchId, request.Label, request.DestinationUrl,
                    request.Token), cancellationToken);
            return Results.Created($"/api/v1/branch-connections/{result.InstallationId}", result);
        }
        catch (ArgumentException)
        { return Error(400, "INVALID_BRANCH_CONNECTION", "Datos de conexión inválidos."); }
        catch (CentralBranchConnectionConflictException)
        { return Error(409, "BRANCH_CONNECTION_CONFLICT", "La sucursal ya está registrada."); }
        catch (CentralBranchConnectionVerificationException)
        { return Error(422, "BRANCH_CONNECTION_UNVERIFIED", "No se pudo verificar la sucursal."); }
        catch (InvalidOperationException)
        { return Error(503, "BRANCH_CREDENTIAL_UNAVAILABLE", "Credenciales centrales no disponibles."); }
    }

    private static bool TryReadPage(IQueryCollection query, out int page, out int pageSize)
    {
        page = 1;
        pageSize = 20;
        if (query.Keys.Any(key => key is not ("page" or "pageSize"))) return false;
        if (query.TryGetValue("page", out var pageValues)
            && (pageValues.Count != 1 || !int.TryParse(pageValues[0],
                NumberStyles.None, CultureInfo.InvariantCulture, out page))) return false;
        if (query.TryGetValue("pageSize", out var sizeValues)
            && (sizeValues.Count != 1 || !int.TryParse(sizeValues[0],
                NumberStyles.None, CultureInfo.InvariantCulture, out pageSize))) return false;
        return page >= 1 && pageSize is >= 1 and <= 100
            && (long)(page - 1) * pageSize <= int.MaxValue;
    }

    private static IResult Error(int status, string code, string message) => Results.Json(
        new { error = new { code, message, details = (object?)null } }, statusCode: status);

    private sealed record CreateRequest(Guid InstallationId, Guid RemoteCompanyId,
        Guid RemoteBranchId, string? Label, string? DestinationUrl, string? Token);
    private sealed record EditRequest(string? Label, string? DestinationUrl, string? Token);
    private sealed record StateRequest(bool? IsActive);
}
