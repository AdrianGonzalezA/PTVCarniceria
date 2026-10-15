using System.Threading.RateLimiting;
using Carnicerias.Central.Api;
using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICentralPasswordHasher>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("CentralAccess")
        ?? Environment.GetEnvironmentVariable("CARNICERIAS_CENTRAL_CONNECTION_STRING");
    return new CentralPasswordHasher(CentralPasswordPolicy.MinimumLength(connectionString));
});
builder.Services.AddScoped<ICentralCompanyManagementService, CentralCompanyManagementService>();
builder.Services.AddScoped<ICentralSessionStore, EfCentralSessionStore>();
builder.Services.AddScoped<ICentralManufacturerSessionStore, EfCentralManufacturerSessionStore>();
builder.Services.AddScoped<CentralManufacturerSessionService>();
builder.Services.AddScoped<ICentralInstallationReader, EfCentralInstallationReader>();
builder.Services.AddScoped<ICentralBusinessInstallationReader, EfCentralBusinessInstallationReader>();
builder.Services.AddSingleton<ICentralBranchCredentialSource, EnvironmentCentralBranchCredentialSource>();
builder.Services.AddSingleton<ICentralDnsResolver, CentralDnsResolver>();
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var approved = CentralApprovedEndpointConfiguration.Parse(
        configuration["CARNICERIAS_CENTRAL_APPROVED_BRANCHES"], allowMissing: true);
    var allowLoopbackHttp = configuration["CARNICERIAS_CENTRAL_ALLOW_LOOPBACK_HTTP"] == "true";
    return new CentralDestinationPolicy(approved, allowLoopbackHttp,
        services.GetRequiredService<ICentralDnsResolver>());
});
builder.Services.AddScoped<ICentralBranchSummarySource, CentralLiveBranchSummarySource>();
builder.Services.AddScoped<CentralBusinessSummaryService>();
builder.Services.AddScoped<CentralSessionService>();
builder.Services.AddSingleton<CentralLoginLimiter>();
// ASP.NET Core 10 rate-limiting policy: https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("central-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(15),
            AutoReplenishment = true
        }));
    options.AddPolicy("central-business", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1),
            AutoReplenishment = true
        }));
    options.OnRejected = async (rateLimitContext, cancellationToken) =>
    {
        rateLimitContext.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var business = rateLimitContext.HttpContext.Request.Path
            .StartsWithSegments("/api/v1/business");
        await rateLimitContext.HttpContext.Response.WriteAsJsonAsync(
            new { error = new { code = business ? "QUERY_RATE_LIMITED" : "LOGIN_RATE_LIMITED",
                message = business ? "Demasiadas consultas. Reintentá más tarde."
                    : "Demasiados intentos. Reintentá más tarde.", details = (object?)null } },
            cancellationToken);
    };
});
builder.Services.AddDbContext<CentralAccessDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("CentralAccess")
        ?? Environment.GetEnvironmentVariable("CARNICERIAS_CENTRAL_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Configure the central application database explicitly.");
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "central_access"));
});
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'";
    if (context.Request.Path.StartsWithSegments("/api/v1/sessions")
        || context.Request.Path.StartsWithSegments("/api/v1/manufacturer")
        || context.Request.Path.StartsWithSegments("/api/v1/installations")
        || context.Request.Path.StartsWithSegments("/api/v1/business"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    await next(context);
});
app.UseRouting();
app.UseRateLimiter();
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));
app.MapCentralSessionEndpoints();
app.MapCentralManufacturerSessionEndpoints();
app.MapCentralManufacturerCompanyEndpoints();
app.MapCentralInstallationEndpoints();
app.MapCentralBusinessSummaryEndpoints();

app.Run();

public partial class Program;
