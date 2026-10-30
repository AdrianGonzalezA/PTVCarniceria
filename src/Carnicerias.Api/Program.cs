using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Carnicerias.Api.Sessions;
using Carnicerias.Api.Security;
using Carnicerias.Api.Users;
using Carnicerias.Api.Catalog;
using Carnicerias.Api.Sales;
using Carnicerias.Api.Inventory;
using Carnicerias.Api.Pos;
using Carnicerias.Api.Organization;
using Carnicerias.Api.History;
using Carnicerias.Api.Fiscal;
using Carnicerias.Api.Customers;
using Carnicerias.Api.ExcelImport;
using Carnicerias.Api.Payments;
using Carnicerias.Api.Federation;
using Carnicerias.Api.Contracts;
using Carnicerias.Domain.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.GetValue<bool>("Clover:TunnelOnly"))
{
    // The tunnel is a separate process; Windows EventLog is not writable by the dev account.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
}
builder.Services.AddDbContext<PlatformAccessDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("PlatformAccess")
        ?? Environment.GetEnvironmentVariable("CARNICERIAS_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Configure the application database explicitly before accessing it.");
    options.UseNpgsql(connectionString);
});
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => BranchServiceSettings.FromConfiguration(
    services.GetRequiredService<IConfiguration>()));
builder.Services.AddDataProtection().SetApplicationName("Carnicerias.Fiscal");
builder.Services.AddHttpClient<ArcaWsaaClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddHttpClient<ArcaWsfeClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddHttpClient<MercadoPagoPointClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<MercadoPagoQrClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<MercadoPagoStaticQrClient>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<MercadoPagoOrderLookupClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<CloverOAuthClient>(client => client.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<CloverMerchantProbeClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<CloverDeviceInventoryClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<ArcaHomologationTicketProvider>();
builder.Services.AddScoped<ArcaSettingsResolver>();
builder.Services.AddScoped<MercadoPagoSettingsResolver>();
builder.Services.AddScoped<CloverSettingsProtector>();
builder.Services.AddScoped<CloverAccessTokenResolver>();
builder.Services.AddScoped<SessionAuthenticationService>();
builder.Services.AddScoped<PosTerminalAuthenticationService>();
builder.Services.AddScoped<PosTerminalProvisioningService>();
builder.Services.AddScoped<OperationalContextAccessService>();
builder.Services.AddScoped<OperationalContextAccessor>();
builder.Services.AddScoped<OperationalContextChangeGuard>();
builder.Services.AddScoped<IOperationalContextChangeBlocker, CashierShiftContextChangeBlocker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (rejection, cancellationToken) =>
    {
        if (!rejection.HttpContext.Request.Path.StartsWithSegments("/api/central/v1")) return;
        rejection.HttpContext.Response.Headers.CacheControl = "no-store";
        await rejection.HttpContext.Response.WriteAsJsonAsync(
            new ErrorResponse(new ApiError("RATE_LIMITED", "No se pudo completar la solicitud", [])),
            cancellationToken);
    };
    options.AddFixedWindowLimiter("clover-public", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.AddFixedWindowLimiter("branch-service", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.AddFixedWindowLimiter("branch-central-credential", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();
var branchServiceSettings = app.Services.GetRequiredService<BranchServiceSettings>();
if (branchServiceSettings.IsRequested && !branchServiceSettings.IsEnabled)
    BranchServiceEndpoints.LogConfigurationIncomplete(app.Logger);
var serveWeb = !app.Configuration.GetValue<bool>("Clover:TunnelOnly") &&
    app.Environment.WebRootPath is { } webRoot &&
    File.Exists(Path.Combine(webRoot, "index.html"));

if (serveWeb)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseRouting();
app.UseRateLimiter();

if (app.Configuration.GetValue<bool>("Clover:TunnelOnly"))
{
    app.Use(async (context, next) =>
    {
        if (!CloverTunnelBoundary.Allows(context.Request.Method, context.Request.Path.Value))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        await next(context);
    });

    app.MapPublicCloverOAuthEndpoints();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));
if (!app.Configuration.GetValue<bool>("Clover:TunnelOnly"))
    app.MapBranchServiceEndpoints(branchServiceSettings);
if (!app.Configuration.GetValue<bool>("Clover:TunnelOnly"))
    app.MapAdminBranchCentralConnectionEndpoints(branchServiceSettings);
app.MapSessionEndpoints();
app.MapManufacturerEndpoints();
app.MapUserEndpoints();
app.MapCatalogEndpoints();
app.MapAdminCategoryEndpoints();
app.MapAdminProductEndpoints();
app.MapAdminProductTaxEndpoints();
app.MapAdminTaxCatalogEndpoints();
app.MapAdminTaxAssignmentEndpoints();
app.MapAdminProductCodeEndpoints();
app.MapAdminPriceListEndpoints();
app.MapAdminProductPriceEndpoints();
app.MapAdminOrganizationEndpoints();
app.MapAdminArcaSettingsEndpoints();
app.MapAdminMercadoPagoSettingsEndpoints();
app.MapAdminCloverOAuthEndpoints();
app.MapAdminTerminalEndpoints();
app.MapAdminBranchDeviceEndpoints();
app.MapAdminHistoryEndpoints();
app.MapAdminCustomerEndpoints();
app.MapAdminExcelImportEndpoints();
app.MapPosCustomerEndpoints();
app.MapCustomerCollectionEndpoints();
app.MapCustomerCollectionCorrectionEndpoints();
app.MapAdminAccountEndpoints();
app.MapSaleDraftEndpoints();
app.MapSaleConfirmationEndpoints();
app.MapMercadoPagoPaymentEndpoints();
app.MapIntegratedGatewayAvailabilityEndpoints();
app.MapMercadoPagoWebhookEndpoints();
app.MapFiscalDocumentEndpoints();
app.MapInventoryEndpoints();
app.MapBarcodePreviewEndpoints();
app.MapBarcodeProfileEndpoints();
app.MapInventoryPieceEndpoints();
app.MapCashierShiftEndpoints();
app.MapPosTerminalEndpoints();

if (serveWeb)
{
    // Unknown API paths must not return the Angular shell with HTTP 200.
    app.MapFallback("/api/{**path}", () => Results.NotFound());
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
