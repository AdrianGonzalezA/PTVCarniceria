using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Carnicerias.Api.Sessions;
using Carnicerias.Api.Security;
using Carnicerias.Api.Users;
using Carnicerias.Api.Catalog;
using Carnicerias.Api.Sales;
using Carnicerias.Api.Inventory;
using Carnicerias.Api.Pos;
using Carnicerias.Domain.PlatformAccess;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("PlatformAccess")
    ?? Environment.GetEnvironmentVariable("CARNICERIAS_CONNECTION_STRING")
    ?? "Host=localhost;Database=carnicerias_design;Username=postgres";

builder.Services.AddDbContext<PlatformAccessDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SessionAuthenticationService>();
builder.Services.AddScoped<PosTerminalAuthenticationService>();
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
app.MapAdminProductCodeEndpoints();
app.MapSaleDraftEndpoints();
app.MapSaleConfirmationEndpoints();
app.MapInventoryEndpoints();
app.MapCashierShiftEndpoints();
app.MapPosTerminalEndpoints();

app.Run();

public partial class Program;
