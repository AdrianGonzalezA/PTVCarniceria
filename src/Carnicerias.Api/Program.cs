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
using Carnicerias.Domain.PlatformAccess;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddHttpClient<ArcaWsaaClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddHttpClient<ArcaWsfeClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddSingleton<ArcaHomologationTicketProvider>();
builder.Services.AddScoped<SessionAuthenticationService>();
builder.Services.AddScoped<PosTerminalAuthenticationService>();
builder.Services.AddScoped<PosTerminalProvisioningService>();
builder.Services.AddScoped<OperationalContextAccessService>();
builder.Services.AddScoped<OperationalContextAccessor>();
builder.Services.AddScoped<OperationalContextChangeGuard>();
builder.Services.AddScoped<IOperationalContextChangeBlocker, CashierShiftContextChangeBlocker>();

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));
app.MapSessionEndpoints();
app.MapUserEndpoints();
app.MapCatalogEndpoints();
app.MapAdminCategoryEndpoints();
app.MapAdminProductEndpoints();
app.MapAdminProductTaxEndpoints();
app.MapAdminProductCodeEndpoints();
app.MapAdminPriceListEndpoints();
app.MapAdminProductPriceEndpoints();
app.MapAdminOrganizationEndpoints();
app.MapAdminTerminalEndpoints();
app.MapAdminHistoryEndpoints();
app.MapAdminCustomerEndpoints();
app.MapPosCustomerEndpoints();
app.MapCustomerCollectionEndpoints();
app.MapCustomerCollectionCorrectionEndpoints();
app.MapAdminAccountEndpoints();
app.MapSaleDraftEndpoints();
app.MapSaleConfirmationEndpoints();
app.MapFiscalDocumentEndpoints();
app.MapInventoryEndpoints();
app.MapBarcodePreviewEndpoints();
app.MapBarcodeProfileEndpoints();
app.MapInventoryPieceEndpoints();
app.MapCashierShiftEndpoints();
app.MapPosTerminalEndpoints();

app.Run();

public partial class Program;
