using System.Globalization;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Api;

public static class CentralInstallationEndpoints
{
    public static void MapCentralInstallationEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/installations", ListAsync);

    private static async Task<IResult> ListAsync(HttpContext http,
        CentralSessionService sessionService,
        ICentralInstallationReader reader,
        CancellationToken cancellationToken)
    {
        var session = await sessionService.AuthenticateAsync(
            http.Request.Cookies[CentralSessionEndpoints.CookieName], cancellationToken);
        if (session is null)
        {
            return Error(401, "SESSION_REQUIRED", "SesiÃ³n central requerida.");
        }

        if (!TryReadPage(http.Request.Query, out var page, out var pageSize))
        {
            return Error(400, "INVALID_PAGINATION", "PaginaciÃ³n invÃ¡lida.");
        }

        var result = await reader.ReadAsync(session.Company.Id, page, pageSize, cancellationToken);
        return Results.Ok(new
        {
            data = result.Data,
            pagination = new
            {
                page = result.Page,
                pageSize = result.PageSize,
                totalCount = result.TotalCount,
                totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize)
            }
        });
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

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new { error = new { code, message, details = (object?)null } },
            statusCode: statusCode);
}
