using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Payments;

public static class AdminMercadoPagoSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminMercadoPagoSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/payment-gateways/mercado-pago")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        group.MapGet("", GetAsync);
        group.MapPut("", SaveAsync);
        group.MapPut("/registers/{terminalId:guid}", SaveRegisterAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var companyId = accessor.Context.CompanyId;
        var settings = await db.MercadoPagoGatewaySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        var registers = await (from terminal in db.PosTerminals.AsNoTracking()
            join branch in db.Branches.AsNoTracking() on terminal.BranchId equals branch.Id
            join mapping in db.MercadoPagoRegisterSettings.AsNoTracking()
                on terminal.Id equals mapping.PosTerminalId into mappings
            from mapping in mappings.DefaultIfEmpty()
            where terminal.CompanyId == companyId && branch.CompanyId == companyId && !terminal.IsHistorical
            orderby branch.Name, terminal.Name
            select new { terminal.Id, terminal.Name, terminal.BranchId, BranchName = branch.Name,
                terminal.IsActive, QrExternalPosId = mapping == null ? null : mapping.QrExternalPosId,
                PointTerminalId = mapping == null ? null : mapping.PointTerminalId }).ToArrayAsync(cancellationToken);
        return Results.Ok(new { provider = "mercadoPago", environment = "test",
            sellerUserId = settings?.SellerUserId ?? "", hasAccessToken = settings?.HasAccessToken ?? false,
            hasWebhookSecret = settings?.HasWebhookSecret ?? false, registers });
    }

    private static async Task<IResult> SaveAsync(GatewayRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        MercadoPagoSettingsResolver resolver, TimeProvider clock,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.AccessToken?.Length > 4096 ||
            request.WebhookSecret?.Length > 4096 ||
            request.AccessToken is { Length: > 0 } token &&
                (!token.StartsWith("APP_USR-", StringComparison.Ordinal) || token.Any(char.IsWhiteSpace)) ||
            request.WebhookSecret is { Length: > 0 } secret && secret.Any(char.IsWhiteSpace))
            return Error(400, "VALIDATION_ERROR");
        var companyId = accessor.Context.CompanyId;
        var actorId = accessor.Context.UserId;
        var now = clock.GetUtcNow();
        var settings = await db.MercadoPagoGatewaySettings.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId, cancellationToken);
        try
        {
            if (settings is null)
            {
                settings = new MercadoPagoGatewaySettings(companyId, request.SellerUserId, actorId, now);
                db.MercadoPagoGatewaySettings.Add(settings);
            }
            else settings.UpdateSeller(request.SellerUserId, actorId, now);
        }
        catch (ArgumentException) { return Error(400, "VALIDATION_ERROR"); }
        if (!string.IsNullOrEmpty(request.AccessToken))
            settings.ReplaceAccessToken(resolver.Protect(request.AccessToken), actorId, now);
        if (!string.IsNullOrEmpty(request.WebhookSecret))
            settings.ReplaceWebhookSecret(resolver.Protect(request.WebhookSecret), actorId, now);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { settings.SellerUserId, settings.HasAccessToken, settings.HasWebhookSecret });
    }

    private static async Task<IResult> SaveRegisterAsync(Guid terminalId, RegisterRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, TimeProvider clock,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return Error(403, "CSRF_REJECTED");
        if (request is null || terminalId == Guid.Empty) return Error(400, "VALIDATION_ERROR");
        var companyId = accessor.Context.CompanyId;
        var terminal = await db.PosTerminals.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == terminalId && !item.IsHistorical,
            cancellationToken);
        if (terminal is null) return Error(404, "REGISTER_NOT_FOUND");
        var settings = await db.MercadoPagoRegisterSettings.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.PosTerminalId == terminalId, cancellationToken);
        try
        {
            if (settings is null)
            {
                settings = new MercadoPagoRegisterSettings(companyId, terminal.BranchId, terminal.Id,
                    EmptyToNull(request.QrExternalPosId), EmptyToNull(request.PointTerminalId),
                    accessor.Context.UserId, clock.GetUtcNow());
                db.MercadoPagoRegisterSettings.Add(settings);
            }
            else settings.Update(EmptyToNull(request.QrExternalPosId),
                EmptyToNull(request.PointTerminalId), accessor.Context.UserId, clock.GetUtcNow());
        }
        catch (ArgumentException) { return Error(400, "VALIDATION_ERROR"); }
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return Error(409, "REGISTER_IDENTIFIER_CONFLICT"); }
        return Results.Ok(new { terminalId, settings.QrExternalPosId, settings.PointTerminalId });
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int status, string code) => Results.Json(new
    {
        error = new { code, message = "No se pudo guardar la pasarela de cobro" }
    }, statusCode: status);

    private sealed record GatewayRequest(string SellerUserId, string? AccessToken, string? WebhookSecret);
    private sealed record RegisterRequest(string? QrExternalPosId, string? PointTerminalId);
}
