using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Api;

public static class CentralBusinessSummaryEndpoints
{
    public static void MapCentralBusinessSummaryEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/business/summary", ReadAsync)
            .RequireRateLimiting("central-business");

    private static async Task<IResult> ReadAsync(HttpContext http,
        CentralSessionService sessions, CentralBusinessSummaryService summaries,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var session = await sessions.AuthenticateAsync(
            http.Request.Cookies[CentralSessionEndpoints.CookieName], cancellationToken);
        if (session is null)
            return Error(401, "SESSION_REQUIRED", "Sesión central requerida.");

        var query = http.Request.Query;
        CentralBusinessWindow window;
        if (query.Count == 0)
        {
            window = CentralBusinessWindow.Today(clock.GetUtcNow());
        }
        else if (query.Count == 2 && query.TryGetValue("fromUtc", out var from)
                 && query.TryGetValue("toUtc", out var to)
                 && from.Count == 1 && to.Count == 1
                 && CentralBusinessWindow.TryCreate(from[0], to[0], out window))
        {
            // Exact validated UTC window, independent of the browser's time zone.
        }
        else if (query.Count == 2 && query.TryGetValue("fromDate", out var fromDate)
                 && query.TryGetValue("toDate", out var toDate)
                 && fromDate.Count == 1 && toDate.Count == 1
                 && CentralBusinessWindow.TryCreateLocalDates(fromDate[0], toDate[0], out window))
        {
            // Inclusive Argentina calendar dates become an exclusive UTC window.
        }
        else
        {
            return Error(400, "INVALID_WINDOW", "Período de consulta inválido.");
        }

        try
        {
            return Results.Ok(await summaries.ReadAsync(
                session.Company.Id, window, cancellationToken));
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return Error(503, "SUMMARY_UNAVAILABLE", "No se pudo completar la consulta central.");
        }
    }

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new { error = new { code, message, details = (object?)null } },
            statusCode: statusCode);
}
