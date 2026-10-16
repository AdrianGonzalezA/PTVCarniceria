using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Payments;

public sealed record AvailableIntegratedGateway(string Provider, IReadOnlyList<string> Modes);

public static class IntegratedGatewayAvailability
{
    public static IReadOnlyList<AvailableIntegratedGateway> ForRegister(
        MercadoPagoGatewaySettings? seller, MercadoPagoRegisterSettings? register)
    {
        if (seller?.HasAccessToken != true || register is null ||
            seller.CompanyId != register.CompanyId)
            return [];
        var modes = new List<string>(3);
        if (register.QrExternalPosId is not null)
        {
            modes.Add("qr");
            modes.Add("qrStatic");
        }
        if (register.PointTerminalId is not null) modes.Add("point");
        return modes.Count == 0 ? [] : [new AvailableIntegratedGateway("mercadoPago", modes)];
    }
}

public static class IntegratedGatewayAvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapIntegratedGatewayAvailabilityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/pos/payment-gateways", GetAsync).RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (accessor.TerminalId is not Guid terminalId)
            return Results.Ok(new { gateways = Array.Empty<AvailableIntegratedGateway>() });

        var context = accessor.Context;
        var hasShift = await db.CashierShifts.AsNoTracking().AnyAsync(shift =>
            shift.CompanyId == context.CompanyId && shift.BranchId == context.BranchId &&
            shift.PosTerminalId == terminalId && shift.CashierId == context.UserId &&
            shift.Status == CashierShiftStatus.Open, cancellationToken);
        if (!hasShift) return Results.Ok(new { gateways = Array.Empty<AvailableIntegratedGateway>() });

        var seller = await db.MercadoPagoGatewaySettings.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId, cancellationToken);
        var register = await db.MercadoPagoRegisterSettings.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.BranchId == context.BranchId &&
            item.PosTerminalId == terminalId, cancellationToken);
        return Results.Ok(new { gateways = IntegratedGatewayAvailability.ForRegister(seller, register) });
    }
}
