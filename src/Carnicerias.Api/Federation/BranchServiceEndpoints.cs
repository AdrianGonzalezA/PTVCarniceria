using Carnicerias.Api.Contracts;
using Carnicerias.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Federation;

public static class BranchServiceEndpoints
{
    private const string ManagedInstallationItem = "ManagedBranchInstallationId";
    private static readonly Action<ILogger, Exception?> LogIncompleteConfiguration =
        LoggerMessage.Define(LogLevel.Warning, new EventId(4101, "BranchServiceConfiguration"),
            "Central branch service disabled because its configuration is incomplete.");
    private static readonly Action<ILogger, string, Exception?> LogBindingFailure =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4102, "BranchBindingRead"),
            "Central branch binding could not be read ({ErrorType}).");
    private static readonly Action<ILogger, string, Exception?> LogSummaryFailure =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4103, "BranchSummaryRead"),
            "Central branch summary could not be read ({ErrorType}).");

    public static void LogConfigurationIncomplete(ILogger logger) =>
        LogIncompleteConfiguration(logger, null);

    public static IEndpointRouteBuilder MapBranchServiceEndpoints(
        this IEndpointRouteBuilder endpoints, BranchServiceSettings settings)
    {
        if (!settings.IsEnabled) return endpoints;

        var group = endpoints.MapGroup("/api/central/v1")
            .RequireRateLimiting("branch-service")
            .AddEndpointFilter(async (invocation, next) =>
            {
                var http = invocation.HttpContext;
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                var liveSettings = BranchServiceSettings.FromConfiguration(
                    http.RequestServices.GetRequiredService<IConfiguration>());
                var authorization = http.Request.Headers.Authorization;
                if (!liveSettings.IsEnabled || liveSettings.IsManaged != settings.IsManaged ||
                    (!settings.IsManaged && liveSettings.InstallationId != settings.InstallationId) ||
                    liveSettings.CompanyId != settings.CompanyId ||
                    liveSettings.BranchId != settings.BranchId ||
                    authorization.Count != 1 || authorization[0] is not { } header ||
                    header.Length != 71 ||
                    !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return Error(StatusCodes.Status401Unauthorized, "NOT_AUTHENTICATED");

                if (settings.IsManaged)
                {
                    try
                    {
                        var db = http.RequestServices.GetRequiredService<PlatformAccessDbContext>();
                        var connection = await db.BranchCentralConnections.AsNoTracking()
                            .SingleOrDefaultAsync(item => item.CompanyId == settings.CompanyId &&
                                item.BranchId == settings.BranchId, http.RequestAborted);
                        var now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                        if (connection is null || !connection.VerifyCredential(header[7..], now))
                            return Error(StatusCodes.Status401Unauthorized, "NOT_AUTHENTICATED");
                        http.Items[ManagedInstallationItem] = connection.InstallationId;
                    }
                    catch (Exception exception) when (!http.RequestAborted.IsCancellationRequested)
                    {
                        var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();
                        LogBindingFailure(logger, exception.GetType().Name, null);
                        return Error(StatusCodes.Status503ServiceUnavailable,
                            "BRANCH_BINDING_UNAVAILABLE");
                    }
                }
                else if (!liveSettings.VerifyCredential(header[7..]))
                    return Error(StatusCodes.Status401Unauthorized, "NOT_AUTHENTICATED");

                return await next(invocation);
            });

        group.MapGet("/installation", (HttpContext http, CancellationToken cancellationToken) =>
            GetInstallationAsync(http, settings, cancellationToken));
        group.MapGet("/summary", (HttpContext http, CancellationToken cancellationToken) =>
            GetSummaryAsync(http, settings, cancellationToken));
        return endpoints;
    }

    private static async Task<IResult> GetInstallationAsync(HttpContext http,
        BranchServiceSettings settings, CancellationToken cancellationToken)
    {
        if (http.Request.Query.Count != 0)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var binding = await LoadBindingAsync(http, settings, timeout.Token);
            if (binding is null)
                return Error(StatusCodes.Status503ServiceUnavailable, "BRANCH_BINDING_UNAVAILABLE");

            return Results.Ok(new BranchInstallationResponse(1, InstallationId(http, settings),
                settings.CompanyId, binding.CompanyName, settings.BranchId, binding.BranchName,
                ["summary"]));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "BRANCH_BINDING_UNAVAILABLE");
        }
    }

    private static async Task<IResult> GetSummaryAsync(HttpContext http,
        BranchServiceSettings settings, CancellationToken cancellationToken)
    {
        var query = http.Request.Query;
        if (query.Count != 2 || !query.TryGetValue("fromUtc", out var from) ||
            !query.TryGetValue("toUtc", out var to) || from.Count != 1 || to.Count != 1 ||
            !BranchSummaryWindow.TryCreate(from[0], to[0], out var window))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            if (await LoadBindingAsync(http, settings, timeout.Token) is null)
                return Error(StatusCodes.Status503ServiceUnavailable, "BRANCH_BINDING_UNAVAILABLE");

            var db = http.RequestServices.GetRequiredService<PlatformAccessDbContext>();
            var data = await BranchSummaryQuery.ReadAsync(db, settings.CompanyId,
                settings.BranchId, window, timeout.Token);
            var generatedAtUtc = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            return Results.Ok(new BranchSummaryResponse(1, InstallationId(http, settings),
                settings.CompanyId, settings.BranchId, window.FromUtc, window.ToUtc,
                generatedAtUtc, "ARS", data.SaleCount, data.SalesTotal,
                data.SalePaymentsTotal, data.PaymentsByMethod, data.OpenShiftCount,
                data.OpenShiftCashBalance));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();
            LogSummaryFailure(logger, exception.GetType().Name, null);
            return Error(StatusCodes.Status503ServiceUnavailable, "SUMMARY_UNAVAILABLE");
        }
    }

    private static async Task<BranchBinding?> LoadBindingAsync(HttpContext http,
        BranchServiceSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var db = http.RequestServices.GetRequiredService<PlatformAccessDbContext>();
            return await db.Branches.AsNoTracking()
                .Where(branch => branch.Id == settings.BranchId &&
                    branch.CompanyId == settings.CompanyId && branch.IsActive)
                .Join(db.Companies.AsNoTracking().Where(company => company.IsActive),
                    branch => branch.CompanyId, company => company.Id,
                    (branch, company) => new BranchBinding(company.Name, branch.Name))
                .SingleOrDefaultAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();
            LogBindingFailure(logger, exception.GetType().Name, null);
            return null;
        }
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private static Guid InstallationId(HttpContext http, BranchServiceSettings settings) =>
        settings.IsManaged ? (Guid)http.Items[ManagedInstallationItem]! : settings.InstallationId;

    private sealed record BranchBinding(string CompanyName, string BranchName);
    private sealed record BranchInstallationResponse(int ContractVersion, Guid InstallationId,
        Guid CompanyId, string CompanyName, Guid BranchId, string BranchName,
        string[] Capabilities);
    private sealed record BranchSummaryResponse(int ContractVersion, Guid InstallationId,
        Guid CompanyId, Guid BranchId, DateTimeOffset FromUtc, DateTimeOffset ToUtc,
        DateTimeOffset GeneratedAtUtc, string Currency, int SaleCount, decimal SalesTotal,
        decimal SalePaymentsTotal, BranchPaymentSummary[] PaymentsByMethod,
        int OpenShiftCount, decimal OpenShiftCashBalance);
}
